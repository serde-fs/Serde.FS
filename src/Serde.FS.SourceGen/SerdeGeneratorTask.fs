namespace Serde.FS.SourceGen

open System.IO
open Microsoft.Build.Utilities
open Microsoft.Build.Framework

type SerdeGeneratorTask() =
    inherit Task()

    [<Required>]
    member val SourceFiles : ITaskItem array = [||] with get, set

    member val OutputDir : string = "" with get, set

    override this.Execute() =
        try
            if not (Directory.Exists(this.OutputDir)) then
                Directory.CreateDirectory(this.OutputDir) |> ignore

            let mutable success = true

            for item in this.SourceFiles do
                let filePath = item.ItemSpec

                if File.Exists(filePath) && filePath.EndsWith(".fs") then
                    try
                        let types = AstParser.parseFile filePath

                        for typeInfo in types do
                            let code = CodeEmitter.emit typeInfo
                            let outputFile = Path.Combine(this.OutputDir, sprintf "%s.serde.g.fs" typeInfo.TypeName)
                            let existingContent =
                                if File.Exists(outputFile) then Some (File.ReadAllText(outputFile))
                                else None

                            // Only write if content changed (deterministic output)
                            match existingContent with
                            | Some existing when existing = code -> ()
                            | _ -> File.WriteAllText(outputFile, code)

                            this.Log.LogMessage(MessageImportance.Low, "Serde: Generated {0}", outputFile)
                    with ex ->
                        this.Log.LogWarning("Serde: Failed to process {0}: {1}", filePath, ex.Message)

            success
        with ex ->
            this.Log.LogErrorFromException(ex)
            false
