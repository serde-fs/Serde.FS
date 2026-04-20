module Serde.FS.Json.GeneratorHost.Program

open System.IO
open Serde.FS.SourceGen
open Serde.FS.Json.SourceGen

[<EntryPoint>]
let main argv =
    if argv.Length = 0 then
        eprintfn "Expected project directory argument"
        1
    else
        let projectDir = argv[0]
        let outputDir =
            if argv.Length > 1 then argv[1]
            else Path.Combine(projectDir, "obj", "serde-generated")

        if not (Directory.Exists outputDir) then
            Directory.CreateDirectory outputDir |> ignore

        // Discover all .fs source files (excluding generated files)
        let localSourceFiles =
            Directory.GetFiles(projectDir, "*.fs", SearchOption.TopDirectoryOnly)
            |> Array.filter (fun f ->
                let name = Path.GetFileName(f)
                not (name.EndsWith(".serde.g.fs")) && not (name.EndsWith(".djinn.g.fs")) && not (name.EndsWith(".json.g.fs")))
            |> Array.map (fun f -> f, File.ReadAllText f)
            |> Array.toList

        // Read referenced project source files (for [<RpcApi>] discovery)
        let localFullPaths =
            localSourceFiles
            |> List.map (fun (f, _) -> Path.GetFullPath(f).ToLowerInvariant())
            |> Set.ofList

        let refSourceFiles =
            if argv.Length > 2 && not (System.String.IsNullOrWhiteSpace(argv[2])) then
                argv[2].Split(';', System.StringSplitOptions.RemoveEmptyEntries)
                |> Array.filter (fun f ->
                    f.EndsWith(".fs") && File.Exists(f))
                |> Array.filter (fun f ->
                    let name = Path.GetFileName(f)
                    not (name.EndsWith(".serde.g.fs"))
                    && not (name.EndsWith(".djinn.g.fs"))
                    && not (name.EndsWith(".json.g.fs")))
                // Exclude files already in local sources (dedup against MSBuild glob misfires)
                |> Array.filter (fun f ->
                    not (localFullPaths.Contains(Path.GetFullPath(f).ToLowerInvariant())))
                |> Array.map (fun f -> f, File.ReadAllText f)
                |> Array.toList
            else []

        let sourceFiles = localSourceFiles @ refSourceFiles

        let emitter = JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
        let result = SerdeGeneratorEngine.generate sourceFiles emitter

        // Report warnings and errors
        for warning in result.Warnings do
            eprintfn "WARNING: %s" warning
        for error in result.Errors do
            eprintfn "ERROR: %s" error

        if not (List.isEmpty result.Errors) then
            1
        else
            let generatedFiles = System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)

            for source in result.Sources do
                let outputFile =
                    match source.AbsolutePath with
                    | Some p -> p
                    | None -> Path.Combine(outputDir, source.HintName)
                let parentDir = Path.GetDirectoryName(outputFile)
                if not (System.String.IsNullOrEmpty parentDir) && not (Directory.Exists parentDir) then
                    Directory.CreateDirectory parentDir |> ignore
                let existingContent =
                    if File.Exists outputFile then Some (File.ReadAllText outputFile)
                    else None
                match existingContent with
                | Some existing when existing = source.Code -> ()
                | _ -> File.WriteAllText(outputFile, source.Code)
                generatedFiles.Add outputFile |> ignore

            // Remove stale generated files (only JSON-owned suffixes)
            if Directory.Exists outputDir then
                for ext in ["*.json.g.fs"] do
                    for existingFile in Directory.GetFiles(outputDir, ext) do
                        if not (generatedFiles.Contains existingFile) then
                            File.Delete existingFile

            0
