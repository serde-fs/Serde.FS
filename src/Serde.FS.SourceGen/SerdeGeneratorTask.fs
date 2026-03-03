namespace Serde.FS.SourceGen

open System.IO
open Serde.FS
open FSharp.SourceDjinn
open FSharp.SourceDjinn.TypeModel.Types
open Microsoft.Build.Utilities
open Microsoft.Build.Framework

module private OptionDiscovery =

    /// Recursively collects all distinct option TypeInfos from a TypeInfo.
    let rec private collectOptionTypes (ti: TypeInfo) (acc: Map<string, TypeInfo>) : Map<string, TypeInfo> =
        match ti.Kind with
        | Option inner ->
            // Collect inner first (dependency order)
            let acc = collectOptionTypes inner acc
            let key = typeInfoToPascalName ti
            if acc |> Map.containsKey key then acc
            else acc |> Map.add key ti
        | Tuple elements ->
            elements |> List.fold (fun a f -> collectOptionTypes f.Type a) acc
        | List inner | Array inner | Set inner ->
            collectOptionTypes inner acc
        | Map (k, v) ->
            let acc = collectOptionTypes k acc
            collectOptionTypes v acc
        | _ -> acc

    /// Scans all record types' fields and returns distinct option TypeInfos in dependency order.
    let discoverOptionTypes (types: SerdeTypeInfo seq) : TypeInfo list =
        let mutable acc = Map.empty
        for t in types do
            match t.Fields with
            | Some fields ->
                for f in fields do
                    acc <- collectOptionTypes f.Type acc
            | None -> ()
        acc |> Map.toList |> List.map snd

    /// Creates a synthetic SerdeTypeInfo for an option TypeInfo.
    let mkOptionSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        {
            Raw = ti
            Capability = Both
            Attributes = SerdeAttributes.empty
            ConverterType = None
            Fields = None
            UnionCases = None
            EnumCases = None
        }

module private TupleDiscovery =

    /// Recursively collects all distinct tuple TypeInfos from a TypeInfo.
    let rec private collectTupleTypes (ti: TypeInfo) (acc: Map<string, TypeInfo>) : Map<string, TypeInfo> =
        match ti.Kind with
        | Tuple elements ->
            // Collect inner tuples first (dependency order)
            let acc = elements |> List.fold (fun a f -> collectTupleTypes f.Type a) acc
            let key = typeInfoToPascalName ti
            if acc |> Map.containsKey key then acc
            else acc |> Map.add key ti
        | Option inner | List inner | Array inner | Set inner ->
            collectTupleTypes inner acc
        | Map (k, v) ->
            let acc = collectTupleTypes k acc
            collectTupleTypes v acc
        | _ -> acc

    /// Scans all types' fields and returns distinct tuple TypeInfos in dependency order.
    let discoverTupleTypes (types: SerdeTypeInfo seq) : TypeInfo list =
        let mutable acc = Map.empty
        for t in types do
            match t.Fields with
            | Some fields ->
                for f in fields do
                    acc <- collectTupleTypes f.Type acc
            | None -> ()
        acc |> Map.toList |> List.map snd

    /// Creates a synthetic SerdeTypeInfo for a tuple TypeInfo.
    let mkTupleSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        {
            Raw = ti
            Capability = Both
            Attributes = SerdeAttributes.empty
            ConverterType = None
            Fields = None
            UnionCases = None
            EnumCases = None
        }

module private FieldTypeResolver =

    /// Recursively resolves unqualified type references in a TypeInfo
    /// using a lookup map built from all parsed types.
    let rec resolveTypeInfo (lookup: Map<string, TypeInfo>) (ti: TypeInfo) : TypeInfo =
        match ti.Kind with
        | Primitive _ -> ti
        | Record _ | AnonymousRecord _ | Union _ | Enum _ ->
            if ti.Namespace.IsNone then
                match Map.tryFind ti.TypeName lookup with
                | Some resolved ->
                    { ti with Namespace = resolved.Namespace; EnclosingModules = resolved.EnclosingModules }
                | None -> ti
            else ti
        | Option inner -> { ti with Kind = Option (resolveTypeInfo lookup inner) }
        | List inner -> { ti with Kind = List (resolveTypeInfo lookup inner) }
        | Array inner -> { ti with Kind = Array (resolveTypeInfo lookup inner) }
        | Set inner -> { ti with Kind = Set (resolveTypeInfo lookup inner) }
        | Map (k, v) -> { ti with Kind = Map (resolveTypeInfo lookup k, resolveTypeInfo lookup v) }
        | Tuple fields ->
            { ti with Kind = Tuple (fields |> List.map (fun f -> { f with Type = resolveTypeInfo lookup f.Type })) }

    let resolveSerdeTypeInfo (lookup: Map<string, TypeInfo>) (sti: SerdeTypeInfo) : SerdeTypeInfo =
        let resolvedFields =
            match sti.Fields with
            | Some fields ->
                Some (fields |> List.map (fun f -> { f with Type = resolveTypeInfo lookup f.Type }))
            | None -> None
        let resolvedUnionCases =
            match sti.UnionCases with
            | Some cases ->
                Some (cases |> List.map (fun c ->
                    { c with Fields = c.Fields |> List.map (fun f -> { f with Type = resolveTypeInfo lookup f.Type }) }))
            | None -> None
        { sti with Fields = resolvedFields; UnionCases = resolvedUnionCases }

module internal NestedTypeValidator =

    let private fullyQualifiedName (ti: TypeInfo) : string =
        let parts =
            [ yield! ti.Namespace |> Option.toList
              yield! ti.EnclosingModules
              yield ti.TypeName ]
        String.concat "." parts

    let rec private validateTypeInfo (serdeNames: Set<string>) (ti: TypeInfo) : string list =
        match ti.Kind with
        | Primitive _ -> []
        | Option inner | List inner | Array inner | Set inner ->
            validateTypeInfo serdeNames inner
        | Map (k, v) ->
            validateTypeInfo serdeNames k @ validateTypeInfo serdeNames v
        | Tuple elements ->
            elements |> List.collect (fun f -> validateTypeInfo serdeNames f.Type)
        | AnonymousRecord fields ->
            fields |> List.collect (fun f -> validateTypeInfo serdeNames f.Type)
        | Record _ | Union _ | Enum _ ->
            let fqn = fullyQualifiedName ti
            if serdeNames.Contains fqn then []
            else
                [ sprintf "Serde error: Type '%s' is used in serialization but does not have Serde metadata. Add [<Serde>] to the type definition." fqn ]

    let validate (serdeNames: Set<string>) (types: SerdeTypeInfo list) : string list =
        let errors = ResizeArray<string>()
        for t in types do
            match t.Fields with
            | Some fields -> for f in fields do errors.AddRange(validateTypeInfo serdeNames f.Type)
            | None -> ()
            match t.UnionCases with
            | Some cases -> for c in cases do for f in c.Fields do errors.AddRange(validateTypeInfo serdeNames f.Type)
            | None -> ()
        errors |> Seq.distinct |> Seq.toList

type SerdeGeneratorTask() =
    inherit Task()

    [<Required>]
    member val SourceFiles : ITaskItem array = [||] with get, set

    member val OutputDir : string = "" with get, set
    member val EmitterAssemblyPath : string = "" with get, set
    member val EmitterTypeName : string = "" with get, set

    member private this.ResolveEmitter() : ISerdeCodeEmitter =
        if not (System.String.IsNullOrEmpty(this.EmitterTypeName)) then
            // Load the emitter assembly into the same AssemblyLoadContext as the task
            // to avoid duplicate type definitions from multiple Serde.FS.dll copies.
            let alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(System.Reflection.Assembly.GetExecutingAssembly())
            let fullPath = System.IO.Path.GetFullPath(this.EmitterAssemblyPath)
            let asm = alc.LoadFromAssemblyPath(fullPath)
            let emitterType = asm.GetType(this.EmitterTypeName)
            System.Activator.CreateInstance(emitterType) :?> ISerdeCodeEmitter
        else
            match SerdeCodegenRegistry.getDefaultEmitter() with
            | Some e -> e
            | None -> failwith "No Serde code emitter registered. Provide EmitterTypeName or call SerdeCodegenRegistry.setDefaultEmitter()."

    override this.Execute() =
        try
            if not (Directory.Exists(this.OutputDir)) then
                Directory.CreateDirectory(this.OutputDir) |> ignore

            let emitter = this.ResolveEmitter()
            let mutable success = true
            let parsedTypes = System.Collections.Generic.List<SerdeTypeInfo>()
            let allTypeInfos = System.Collections.Generic.List<TypeInfo>()
            let allTypes = System.Collections.Generic.List<SerdeTypeInfo>()
            let generatedFiles = System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

            // Phase 1: Parse all source files
            for item in this.SourceFiles do
                let filePath = item.ItemSpec

                if File.Exists(filePath) && filePath.EndsWith(".fs") then
                    try
                        let types = SerdeAstParser.parseFile filePath
                        parsedTypes.AddRange(types)

                        // Also collect ALL type definitions for the lookup map
                        let allTypesInFile = SerdeAstParser.parseFileAllTypes filePath
                        allTypeInfos.AddRange(allTypesInFile)

                    with ex ->
                        this.Log.LogWarning("Serde: Failed to process {0}: {1}", filePath, ex.Message)

            // Phase 2: Resolve field type references across all parsed types
            // Build lookup from ALL types (not just [<Serde>] ones) so that
            // union case fields referencing non-[<Serde>] types get resolved.
            let lookup =
                allTypeInfos
                |> Seq.map (fun t -> t.TypeName, t)
                |> Map.ofSeq
            let resolvedTypes =
                parsedTypes
                |> Seq.map (FieldTypeResolver.resolveSerdeTypeInfo lookup)
                |> Seq.map (fun sti ->
                    match sti.ConverterType with
                    | Some name ->
                        match Map.tryFind name lookup with
                        | Some ti ->
                            let fqn =
                                [ yield! ti.Namespace |> Option.toList
                                  yield! ti.EnclosingModules
                                  yield ti.TypeName ]
                                |> String.concat "."
                            { sti with ConverterType = Some fqn }
                        | None ->
                            // Converter type not in type lookup (e.g. a class);
                            // qualify with the [<Serde>] type's own scope.
                            let prefix =
                                [ yield! sti.Raw.Namespace |> Option.toList
                                  yield! sti.Raw.EnclosingModules ]
                            if prefix.IsEmpty then sti
                            else { sti with ConverterType = Some (String.concat "." (prefix @ [name])) }
                    | None -> sti)
                |> Seq.toList

            // Phase 2.5: Validate nested user-defined types have Serde metadata
            let serdeTypeNames =
                parsedTypes
                |> Seq.map (fun t ->
                    let parts =
                        [ yield! t.Raw.Namespace |> Option.toList
                          yield! t.Raw.EnclosingModules
                          yield t.Raw.TypeName ]
                    String.concat "." parts)
                |> Set.ofSeq
            let violations = NestedTypeValidator.validate serdeTypeNames resolvedTypes
            for msg in violations do
                this.Log.LogError(msg)
            if not (List.isEmpty violations) then
                success <- false

            if success then
                // Phase 3: Emit all types
                for typeInfo in resolvedTypes do
                    let code = SerdeCodeEmitter.emit emitter typeInfo
                    let outputFile = Path.Combine(this.OutputDir, sprintf "%s.serde.g.fs" typeInfo.Raw.TypeName)
                    let existingContent =
                        if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                        else None

                    // Only write if content changed (deterministic output)
                    match existingContent with
                    | Some existing when existing = code -> ()
                    | _ -> File.WriteAllText(outputFile, code)

                    generatedFiles.Add(outputFile) |> ignore
                    this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                    allTypes.Add(typeInfo)

                // Discover and emit option types from record fields
                let optionTypeInfos = OptionDiscovery.discoverOptionTypes allTypes
                for optTi in optionTypeInfos do
                    let optSerdeInfo = OptionDiscovery.mkOptionSerdeTypeInfo optTi
                    let code = SerdeCodeEmitter.emit emitter optSerdeInfo
                    let pascalName = typeInfoToPascalName optTi
                    let outputFile = Path.Combine(this.OutputDir, sprintf "%s.serde.g.fs" pascalName)
                    let existingContent =
                        if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                        else None
                    match existingContent with
                    | Some existing when existing = code -> ()
                    | _ -> File.WriteAllText(outputFile, code)
                    generatedFiles.Add(outputFile) |> ignore
                    this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                    allTypes.Add(optSerdeInfo)

                // Discover and emit tuple types from record fields
                let tupleTypeInfos = TupleDiscovery.discoverTupleTypes allTypes
                for tupTi in tupleTypeInfos do
                    let tupSerdeInfo = TupleDiscovery.mkTupleSerdeTypeInfo tupTi
                    let code = SerdeCodeEmitter.emit emitter tupSerdeInfo
                    let pascalName = typeInfoToPascalName tupTi
                    let outputFile = Path.Combine(this.OutputDir, sprintf "%s.serde.g.fs" pascalName)
                    let existingContent =
                        if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                        else None
                    match existingContent with
                    | Some existing when existing = code -> ()
                    | _ -> File.WriteAllText(outputFile, code)
                    generatedFiles.Add(outputFile) |> ignore
                    this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                    allTypes.Add(tupSerdeInfo)

                // Emit resolver file if the emitter supports it
                let mutable hasResolver = false
                match emitter with
                | :? ISerdeResolverEmitter as resolverEmitter ->
                    match resolverEmitter.EmitResolver(Seq.toList allTypes) with
                    | Some code ->
                        hasResolver <- true
                        let outputFile = Path.Combine(this.OutputDir, "~SerdeResolver.serde.g.fs")
                        let existingContent =
                            if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                            else None
                        match existingContent with
                        | Some existing when existing = code -> ()
                        | _ -> File.WriteAllText(outputFile, code)
                        generatedFiles.Add(outputFile) |> ignore
                        this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                    | None -> ()
                | _ -> ()

                // Emit resolver registration + bootstrap file
                if hasResolver then
                    let code =
                        "// <auto-generated />\n" +
                        "namespace Serde.Generated\n" +
                        "\n" +
                        "module ResolverRegistration =\n" +
                        "    let mutable private initialized = false\n" +
                        "    let registerAll() =\n" +
                        "        if not initialized then\n" +
                        "            initialized <- true\n" +
                        "            SerdeJsonResolver.register()\n" +
                        "\n" +
                        "namespace Djinn.Generated\n" +
                        "\n" +
                        "module Bootstrap =\n" +
                        "    let init () =\n" +
                        "        Serde.ResolverBootstrap.registerAll <- Some Serde.Generated.ResolverRegistration.registerAll\n"
                    let outputFile = Path.Combine(this.OutputDir, "~SerdeResolverRegistration.djinn.g.fs")
                    let existingContent =
                        if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                        else None
                    match existingContent with
                    | Some existing when existing = code -> ()
                    | _ -> File.WriteAllText(outputFile, code)
                    generatedFiles.Add(outputFile) |> ignore
                    this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)

                // Emit entry point wrapper if any source file has [<EntryPoint>]
                for item in this.SourceFiles do
                    let filePath = item.ItemSpec
                    if File.Exists(filePath) && filePath.EndsWith(".fs") then
                        let sourceText = File.ReadAllText(filePath)
                        match EntryPointDetector.detect filePath sourceText with
                        | Some info ->
                            let code = EntryPointEmitter.emit info
                            let outputFile = Path.Combine(this.OutputDir, "~~EntryPoint.djinn.g.fs")
                            let existingContent =
                                if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                                else None
                            match existingContent with
                            | Some existing when existing = code -> ()
                            | _ -> File.WriteAllText(outputFile, code)
                            generatedFiles.Add(outputFile) |> ignore
                            this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                        | None -> ()

                // Remove stale generated files for types that no longer exist
                for existingFile in Directory.GetFiles(this.OutputDir, "*.serde.g.fs") do
                    if not (generatedFiles.Contains(existingFile)) then
                        File.Delete(existingFile)
                        this.Log.LogMessage(MessageImportance.Low, "Serde: Removed stale {0}", existingFile)
                for existingFile in Directory.GetFiles(this.OutputDir, "*.djinn.g.fs") do
                    if not (generatedFiles.Contains(existingFile)) then
                        File.Delete(existingFile)
                        this.Log.LogMessage(MessageImportance.Low, "Serde: Removed stale {0}", existingFile)

            success
        with ex ->
            this.Log.LogErrorFromException(ex)
            false
