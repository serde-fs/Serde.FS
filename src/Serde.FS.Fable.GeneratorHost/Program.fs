module Serde.FS.Fable.GeneratorHost.Program

open System.IO
open System.Text.RegularExpressions
open Serde.FS.SourceGen
open Serde.FS.Fable.SourceGen

// NB: do NOT `open Serde.FS` at the top of this file. The Serde.FS namespace
// exports its own `EntryPointAttribute` (used by the codec runtime), which
// would shadow F#'s `Microsoft.FSharp.Core.EntryPointAttribute` and silently
// break the `[<EntryPoint>]` annotation below — the compiler emits FS0988
// "Main module of program is empty" and the host exe becomes a no-op.

// Entry point invoked from the Serde.FS.Fable buildTransitive target.
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

/// Matches `<PackageReference Include="Serde.FS..." ...>` AND
/// `<ProjectReference Include="..\path\to\Serde.FS*.fsproj" ...>`. Either
/// signals the project participates in Serde's source-gen world and should
/// be scanned by discovery. A project with NEITHER is a UI lib / external
/// dependency we don't want to scan (those typically introduce hundreds of
/// types whose names collide with Domain types — discovery's collision
/// detection ends up firing on the wrong types and codec emission silently
/// loses real Domain types).
let private serdeReferenceRegex =
    Regex(
        @"<(?:Project|Package)Reference\s+Include\s*=\s*""[^""]*Serde\.FS[^""]*""",
        RegexOptions.IgnoreCase)

let private referencesSerdeFS (fsprojPath: string) : bool =
    try
        let xml = File.ReadAllText fsprojPath
        serdeReferenceRegex.IsMatch(xml)
    with _ -> false

/// Walk the ProjectReference graph recursively starting from `fsprojPath`,
/// returning the full set of canonical .fsproj paths reachable AND whose
/// projects reference Serde.FS in some form. The starting project is always
/// included regardless (it's the consumer Fable project — its own sources
/// may not reference Serde.FS directly but they declare types that get
/// serialized via discovered RpcApi interfaces in Shared).
/// Cycles are bounded by the visited set. Logs a warning for any reference
/// whose target file is missing.
let private collectFsprojGraph (rootFsproj: string) : Set<string> =
    let rec walk (isRoot: bool) (visited: Set<string>) (fsprojPath: string) : Set<string> =
        if not (File.Exists fsprojPath) then
            eprintfn "[Serde.FS.Fable] WARNING: project file not found, skipping: %s" fsprojPath
            visited
        else
            let canonical = Path.GetFullPath(fsprojPath).ToLowerInvariant()
            if visited.Contains canonical then visited
            // Skip non-Serde dependencies (UI libraries etc.) — they bring
            // unrelated types into discovery and corrupt collision detection.
            // The root project is exempt because the user always wants their
            // own sources scanned.
            elif not isRoot && not (referencesSerdeFS fsprojPath) then
                eprintfn "[Serde.FS.Fable] Skipping (no Serde.FS reference): %s" fsprojPath
                visited
            else
                let visited = visited.Add canonical
                let dir = Path.GetDirectoryName(Path.GetFullPath fsprojPath)
                try
                    let xml = File.ReadAllText fsprojPath
                    projectRefRegex.Matches(xml)
                    |> Seq.cast<Match>
                    |> Seq.map (fun m -> m.Groups.[1].Value)
                    |> Seq.map (fun rel -> Path.GetFullPath(Path.Combine(dir, rel)))
                    |> Seq.fold (walk false) visited
                with ex ->
                    eprintfn "[Serde.FS.Fable] WARNING: failed to parse ProjectReferences from %s: %s" fsprojPath ex.Message
                    visited
    walk true Set.empty rootFsproj

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
            else collectFsprojGraph consumerFsproj

        // Diagnostic — log the project graph the walker discovered so users
        // (and we, debugging remote setups) can see whether the transitive
        // ProjectReference walk reached every project that declares types
        // their RpcApi methods reference. If types from a project don't
        // appear in DiscoveredTypes, that project's .fsproj is missing
        // from this list and the consumer needs to add a ProjectReference
        // somewhere along the chain. Goes to stdout (not stderr) so it
        // doesn't appear as MSBuild errors.
        printfn "[Serde.FS.Fable] Walked project graph (%d projects):" (Set.count allProjects)
        for p in allProjects do
            printfn "  %s" p

        // Dedup .fs files by canonical path. Multiple ProjectReferences
        // could point to the same project, or directories could nest.
        let sourceFiles =
            allProjects
            |> Set.toList
            |> List.collect collectFsFiles
            |> List.distinctBy (fun (path, _) -> Path.GetFullPath(path).ToLowerInvariant())

        printfn "[Serde.FS.Fable] Collected %d source files." (List.length sourceFiles)

        // Reuse the same discovery used by the server-side generator so the
        // Fable client stays in lockstep with what the server expects.
        let allTypeInfos =
            sourceFiles
            |> List.collect (fun (path, src) ->
                if path.EndsWith ".fs"
                then SerdeAstParser.parseSourceAllTypes path src
                else [])

        let discovery = RpcApiDiscovery.discover allTypeInfos sourceFiles

        // Normalize each discovered type's field TypeInfos via
        // discovery.ResolveFieldType. The server-side `SerdeGeneratorEngine`
        // performs the same normalization on every SerdeTypeInfo it processes;
        // skipping it on the Fable side means field references captured by the
        // parser in unqualified/partial-qualifier form (e.g. `Hub: Forge.Hub`)
        // never reach their canonical (Namespace + EM + TN) shape before the
        // emitter sees them — and the emitter's collision-disambiguation
        // depends on EM being populated. Result: bare codec names like
        // `ConduitCodec` get emitted as field references even though the
        // actual codec module is declared under a disambiguated name like
        // `ConduitSchedule_ConduitCodec`, and compilation fails. We must
        // apply the same `FieldTypeResolver.resolveSerdeTypeInfo` pass here.
        let normalizedTypes =
            discovery.DiscoveredTypes
            |> List.map (FieldTypeResolver.resolveSerdeTypeInfo discovery.ResolveFieldType)

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
                let code = FableClientEmitter.emit rpc normalizedTypes
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
