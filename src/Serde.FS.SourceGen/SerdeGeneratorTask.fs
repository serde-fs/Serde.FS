namespace Serde.FS.SourceGen

open System.IO
open Serde.FS
open Microsoft.Build.Utilities
open Microsoft.Build.Framework

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

                            this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                            allTypes.Add(typeInfo)

                        // Check for entry point registration
                        if not hasEntryPoint then
                            hasEntryPoint <- AstParser.hasEntryPointRegistrationInFile filePath
                    with ex ->
                        this.Log.LogWarning("Serde: Failed to process {0}: {1}", filePath, ex.Message)

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
                this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)

            success
        with ex ->
            this.Log.LogErrorFromException(ex)
            false
