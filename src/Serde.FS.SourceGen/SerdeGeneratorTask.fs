namespace Serde.FS.SourceGen

open System.IO
open Serde.FS
open Microsoft.Build.Utilities
open Microsoft.Build.Framework

module private OptionDiscovery =
    open Serde.FS.TypeKindTypes

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
            Fields = None
            UnionCases = None
            EnumCases = None
        }

module private TupleDiscovery =
    open Serde.FS.TypeKindTypes

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
            Fields = None
            UnionCases = None
            EnumCases = None
        }

module private FieldTypeResolver =
    open Serde.FS.TypeKindTypes

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
            let mutable hasEntryPoint = false
            let parsedTypes = System.Collections.Generic.List<SerdeTypeInfo>()
            let allTypeInfos = System.Collections.Generic.List<TypeKindTypes.TypeInfo>()
            let allTypes = System.Collections.Generic.List<SerdeTypeInfo>()
            let generatedFiles = System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

            // Phase 1: Parse all source files
            for item in this.SourceFiles do
                let filePath = item.ItemSpec

                if File.Exists(filePath) && filePath.EndsWith(".fs") then
                    try
                        let types = AstParser.parseFile filePath
                        parsedTypes.AddRange(types)

                        // Also collect ALL type definitions for the lookup map
                        let allTypesInFile = AstParser.parseFileAllTypes filePath
                        allTypeInfos.AddRange(allTypesInFile)

                        // Check for entry point registration
                        if not hasEntryPoint then
                            hasEntryPoint <- AstParser.hasEntryPointRegistrationInFile filePath
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
                |> Seq.toList

            // Phase 3: Emit all types
            for typeInfo in resolvedTypes do
                let code = CodeEmitter.emit emitter typeInfo
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
                let code = CodeEmitter.emit emitter optSerdeInfo
                let pascalName = TypeKindTypes.typeInfoToPascalName optTi
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
                let code = CodeEmitter.emit emitter tupSerdeInfo
                let pascalName = TypeKindTypes.typeInfoToPascalName tupTi
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
            match emitter with
            | :? ISerdeResolverEmitter as resolverEmitter ->
                match resolverEmitter.EmitResolver(Seq.toList allTypes) with
                | Some code ->
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

            // Emit entry point shim if any source file registers an entry point
            if hasEntryPoint then
                let entryPointCode =
                    "module Serde.Generated.EntryPoint\n\n" +
                    "open Serde.FS\n\n" +
                    "[<EntryPoint>]\n" +
                    "let main argv =\n" +
                    "    // Force all F# module initializers so that entryPoint calls execute\n" +
                    "    for t in System.Reflection.Assembly.GetExecutingAssembly().GetTypes() do\n" +
                    "        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle)\n" +
                    "    SerdeApp.invokeRegisteredEntryPoint argv\n"
                let outputFile = Path.Combine(this.OutputDir, "~~EntryPoint.serde.g.fs")
                let existingContent =
                    if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                    else None
                match existingContent with
                | Some existing when existing = entryPointCode -> ()
                | _ -> File.WriteAllText(outputFile, entryPointCode)
                generatedFiles.Add(outputFile) |> ignore
                this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)

            // Remove stale generated files for types that no longer exist
            for existingFile in Directory.GetFiles(this.OutputDir, "*.serde.g.fs") do
                if not (generatedFiles.Contains(existingFile)) then
                    File.Delete(existingFile)
                    this.Log.LogMessage(MessageImportance.Low, "Serde: Removed stale {0}", existingFile)

            success
        with ex ->
            this.Log.LogErrorFromException(ex)
            false
