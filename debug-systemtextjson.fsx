#r "nuget: Fun.Build, 1.1.17"

open System
open System.IO
open Fun.Build

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let serdeFSProj   = "src/Serde.FS/Serde.FS.fsproj"
let sourceGenProj = "src/Serde.FS.SourceGen/Serde.FS.SourceGen.fsproj"
let stjProj       = "src/Serde.FS.SystemTextJson/Serde.FS.SystemTextJson.fsproj"
let sampleAppProj = "src/Serde.FS.SystemTextJson.SampleApp/Serde.FS.SystemTextJson.SampleApp.fsproj"
let nugetLocalDir = ".nuget-local"

// ---------------------------------------------------------------------------
// Version helpers
// ---------------------------------------------------------------------------

let readVersion (projPath: string) =
    let content = File.ReadAllText(projPath)
    let tag = "<Version>"
    let idx = content.IndexOf(tag)
    if idx = -1 then failwith $"No <Version> found in {projPath}"
    let start = idx + tag.Length
    let endIdx = content.IndexOf("</Version>", start)
    content.Substring(start, endIdx - start).Trim()

let stableVersion = readVersion serdeFSProj
let timestamp     = DateTime.UtcNow.ToString("yyyyMMddTHHmmss")
let debugVersion  = $"{stableVersion}.debug.{timestamp}"

// ---------------------------------------------------------------------------
// Pipeline: debug (default)
// ---------------------------------------------------------------------------

pipeline "debug" {
    description "Pack Serde packages and test the STJ backend via local NuGet feed"

    stage "Show versions" {
        run (fun _ ->
            printfn $"Stable version: {stableVersion}"
            printfn $"Timestamp:      {timestamp}"
            printfn $"Debug version:  {debugVersion}"
        )
    }

    stage "Prune local feed and global cache" {
        run (fun _ ->
            if Directory.Exists(nugetLocalDir) then
                for pkg in Directory.GetFiles(nugetLocalDir, "*.nupkg", SearchOption.AllDirectories) do
                    printfn $"  Deleting {pkg}"
                    File.Delete(pkg)
            else
                Directory.CreateDirectory(nugetLocalDir) |> ignore
            printfn "Local feed pruned."

            // Clear stale debug packages from global NuGet cache
            let globalPkgs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            for pkgName in [ "serde.fs"; "serde.fs.sourcegen"; "serde.fs.systemtextjson" ] do
                let pkgDir = Path.Combine(globalPkgs, pkgName)
                if Directory.Exists(pkgDir) then
                    for versionDir in Directory.GetDirectories(pkgDir) do
                        if Path.GetFileName(versionDir).Contains("debug") then
                            try
                                printfn $"  Clearing global cache: {versionDir}"
                                Directory.Delete(versionDir, true)
                            with :? UnauthorizedAccessException ->
                                printfn $"  Skipped (locked): {versionDir}"
            printfn "Global cache debug versions cleared."
        )
    }

    stage "Pack Serde.FS.SourceGen" {
        run $"dotnet clean {sourceGenProj}"
        run $"dotnet build {sourceGenProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
        run $"dotnet pack {sourceGenProj} -c Debug -o {nugetLocalDir} --no-build /p:NoBuild=true /p:BuildProjectReferences=false /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
    }


    stage "Pack Serde.FS" {
        run $"dotnet clean {serdeFSProj}"
        run $"dotnet build {serdeFSProj} -c Debug /p:PackageVersion={debugVersion}"
        run $"dotnet pack {serdeFSProj} -c Debug -o {nugetLocalDir} --no-build /p:NoBuild=true /p:BuildProjectReferences=false /p:PackageVersion={debugVersion}"
    }


    stage "Pack Serde.FS.SystemTextJson" {
        run $"dotnet clean {stjProj}"
        run $"dotnet build {stjProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SourceGenVersion={debugVersion}"
        run $"dotnet pack {stjProj} -c Debug -o {nugetLocalDir} --no-build /p:NoBuild=true /p:BuildProjectReferences=false /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SourceGenVersion={debugVersion}"
    }

    stage "Restore SampleApp" {
        run $"dotnet restore {sampleAppProj} --no-cache /p:SerdeSTJVersion={debugVersion}"
    }

    stage "Build and run SampleApp" {
        run $"dotnet build {sampleAppProj} --no-restore /p:SerdeSTJVersion={debugVersion}"
        run $"dotnet run --project {sampleAppProj} --no-build"
    }

    stage "Summary" {
        run (fun _ ->
            printfn ""
            printfn "========================================"
            printfn "  Serde Debug Pipeline Summary"
            printfn "========================================"
            printfn $"  Debug version:      {debugVersion}"
            printfn $"  Packed:"
            printfn $"    Serde.FS                  {debugVersion}"
            printfn $"    Serde.FS.SourceGen        {debugVersion}"
            printfn $"    Serde.FS.SystemTextJson   {debugVersion}"
            printfn $"  Restore source:     .nuget-local (--no-cache)"
            printfn $"  SampleApp resolved: {debugVersion}"
            printfn "========================================"
            printfn ""
        )
    }

    runIfOnlySpecified false
}

// ---------------------------------------------------------------------------
// Pipeline: clean
// ---------------------------------------------------------------------------

pipeline "clean" {
    description "Delete debug packages and SampleApp build artifacts"

    stage "Delete local NuGet packages" {
        run (fun _ ->
            if Directory.Exists(nugetLocalDir) then
                for pkg in Directory.GetFiles(nugetLocalDir, "*.nupkg", SearchOption.AllDirectories) do
                    printfn $"  Deleting {pkg}"
                    File.Delete(pkg)
            printfn "Local feed cleaned."
        )
    }

    stage "Delete SampleApp obj/bin" {
        run (fun _ ->
            for dir in [ "src/Serde.FS.SystemTextJson.SampleApp/obj"
                         "src/Serde.FS.SystemTextJson.SampleApp/bin" ] do
                if Directory.Exists(dir) then
                    printfn $"  Deleting {dir}"
                    Directory.Delete(dir, true)
        )
    }

    runIfOnlySpecified
}

tryPrintPipelineCommandHelp ()
