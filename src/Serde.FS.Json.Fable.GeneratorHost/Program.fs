module Serde.FS.Json.Fable.GeneratorHost.Program

open System.IO
open System.Text.RegularExpressions
open Serde.FS.SourceGen
open Serde.FS.Json.Fable.SourceGen

// NB: do NOT `open Serde.FS` at the top of this file. The Serde.FS namespace
// exports its own `EntryPointAttribute` (used by the codec runtime), which
// would shadow F#'s `Microsoft.FSharp.Core.EntryPointAttribute` and silently
// break the `[<EntryPoint>]` annotation below — the compiler emits FS0988
// "Main module of program is empty" and the host exe becomes a no-op.

// Entry point invoked from the Serde.FS.Json.Fable buildTransitive target.
// Args:
//   argv.[0] = projectDir         consumer Fable project's directory.
//   argv.[1] = outputDir          absolute path to <projectDir>/fable-generated.
//   argv.[2] = consumerFsproj     consumer's .fsproj path. The host walks the
//                                 ProjectReference graph from here to gather
//                                 all transitively-reachable source files —
//                                 essential because the consumer's direct
//                                 ProjectReference list isn't enough when
//                                 type references chain (e.g. WebFable →
//                                 Domain → Forge): Forge's source files
//                                 wouldn't be reached by walking only
//                                 WebFable's direct refs.

let private projectRefRegex =
    Regex(@"<ProjectReference\s+Include\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)

/// Walk the ProjectReference graph recursively starting from `fsprojPath`,
/// returning the full set of canonical .fsproj paths reachable. Includes
/// `fsprojPath` itself. Cycles are bounded by the visited set.
let rec private collectFsprojGraph (visited: Set<string>) (fsprojPath: string) : Set<string> =
    if not (File.Exists fsprojPath) then visited
    else
        let canonical = Path.GetFullPath(fsprojPath).ToLowerInvariant()
        if visited.Contains canonical then visited
        else
            let visited = visited.Add canonical
            let dir = Path.GetDirectoryName(Path.GetFullPath fsprojPath)
            try
                let xml = File.ReadAllText fsprojPath
                projectRefRegex.Matches(xml)
                |> Seq.cast<Match>
                |> Seq.map (fun m -> m.Groups.[1].Value)
                |> Seq.map (fun rel -> Path.GetFullPath(Path.Combine(dir, rel)))
                |> Seq.fold collectFsprojGraph visited
            with _ -> visited

/// Collect .fs source files for a project, recursively from its directory.
/// Excludes build artefacts and generated files so we don't try to discover
/// types from already-generated code.
let private collectFsFiles (fsprojPath: string) : (string * string) list =
    let dir = Path.GetDirectoryName(Path.GetFullPath fsprojPath)
    let isExcluded (path: string) =
        let n = path.Replace('\\', '/').ToLowerInvariant()
        n.Contains "/obj/"
        || n.Contains "/bin/"
        || n.Contains "/fable_modules/"
        || n.Contains "/fable-generated/"
        || n.EndsWith(".serde.g.fs")
        || n.EndsWith(".djinn.g.fs")
        || n.EndsWith(".json.g.fs")
        || n.EndsWith(".fable.g.fs")
    Directory.GetFiles(dir, "*.fs", SearchOption.AllDirectories)
    |> Array.filter (fun f -> not (isExcluded f))
    |> Array.map (fun f -> f, File.ReadAllText f)
    |> Array.toList

[<EntryPoint>]
let main (argv: string array) =
    if argv.Length = 0 then
        eprintfn "Expected project directory argument"
        1
    else
        let projectDir = argv.[0]
        let outputDir =
            if argv.Length > 1 then argv.[1]
            else Path.Combine(projectDir, "fable-generated")
        let consumerFsproj =
            if argv.Length > 2 && File.Exists(argv.[2]) then argv.[2]
            else
                // Best-effort: find a .fsproj in projectDir.
                let candidates = Directory.GetFiles(projectDir, "*.fsproj")
                if candidates.Length > 0 then candidates.[0]
                else ""

        if not (Directory.Exists outputDir) then
            Directory.CreateDirectory outputDir |> ignore

        // Build the full project graph the consumer transitively depends on.
        // The consumer's own .fsproj is included so its local sources are
        // gathered as part of the same walk (no need for a separate
        // "local sources" path).
        let allProjects =
            if consumerFsproj = "" then Set.empty
            else collectFsprojGraph Set.empty consumerFsproj

        // Dedup .fs files by canonical path. Multiple ProjectReferences
        // could point to the same project, or directories could nest.
        let sourceFiles =
            allProjects
            |> Set.toList
            |> List.collect collectFsFiles
            |> List.distinctBy (fun (path, _) -> Path.GetFullPath(path).ToLowerInvariant())

        // Reuse the same discovery used by the server-side generator so the
        // Fable client stays in lockstep with what the server expects.
        let allTypeInfos =
            sourceFiles
            |> List.collect (fun (path, src) ->
                if path.EndsWith ".fs"
                then SerdeAstParser.parseSourceAllTypes path src
                else [])

        let discovery = RpcApiDiscovery.discover allTypeInfos sourceFiles

        let generatedFiles =
            System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        let mutable hadErrors = false

        for rpc in discovery.Interfaces do
            // SerdeFS102: every method's input/output TypeInfo must be
            // resolved or we can't compute codec references safely. The
            // validation logic lives on FableClientEmitter so the same
            // diagnostic is unit-testable outside this host.
            match FableClientEmitter.validateInterfaceTypes rpc with
            | Some err ->
                hadErrors <- true
                eprintfn "%s" err
            | None ->
                let code = FableClientEmitter.emit rpc discovery.DiscoveredTypes
                let outputFile = Path.Combine(outputDir, FableClientEmitter.outputFileName rpc)
                let existing =
                    if File.Exists outputFile then Some (File.ReadAllText outputFile)
                    else None
                match existing with
                | Some prev when prev = code -> ()
                | _ -> File.WriteAllText(outputFile, code)
                generatedFiles.Add outputFile |> ignore

        // Self-ignoring .gitignore so generated files don't appear in git.
        let gitignorePath = Path.Combine(outputDir, ".gitignore")
        if not (File.Exists gitignorePath) then
            File.WriteAllText(gitignorePath, "*\n")

        // Delete any stale .fs in the folder (the generator owns it).
        if Directory.Exists outputDir then
            for existingFile in Directory.GetFiles(outputDir, "*.fs") do
                if not (generatedFiles.Contains existingFile) then
                    File.Delete existingFile

        if hadErrors then 1 else 0
