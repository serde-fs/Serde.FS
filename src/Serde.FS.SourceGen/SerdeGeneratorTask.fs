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
        }

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
            let allTypes = System.Collections.Generic.List<SerdeTypeInfo>()
            let generatedFiles = System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

            for item in this.SourceFiles do
                let filePath = item.ItemSpec

                if File.Exists(filePath) && filePath.EndsWith(".fs") then
                    try
                        let types = AstParser.parseFile filePath

                        for typeInfo in types do
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

                        // Check for entry point registration
                        if not hasEntryPoint then
                            hasEntryPoint <- AstParser.hasEntryPointRegistrationInFile filePath
                    with ex ->
                        this.Log.LogWarning("Serde: Failed to process {0}: {1}", filePath, ex.Message)

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

            // Emit resolver file if the emitter supports it
            match emitter with
            | :? ISerdeResolverEmitter as resolverEmitter ->
                match resolverEmitter.EmitResolver(Seq.toList allTypes) with
                | Some code ->
                    let outputFile = Path.Combine(this.OutputDir, "SerdeResolver.serde.g.fs")
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
                let outputFile = Path.Combine(this.OutputDir, "~EntryPoint.serde.g.fs")
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
