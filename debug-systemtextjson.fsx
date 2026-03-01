#r "nuget: Fun.Build, 1.1.17"

open System
open System.IO
open System.Text.RegularExpressions
open Fun.Build

// Usage:
//   dotnet fsi debug-systemtextjson.fsx              runs the "debug" pipeline (default)
//   dotnet fsi debug-systemtextjson.fsx -- -p clean  runs the "clean" pipeline

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let serdeFSProj   = "src/Serde.FS/Serde.FS.fsproj"
let sourceGenProj = "src/Serde.FS.SourceGen/Serde.FS.SourceGen.fsproj"
let stjProj       = "src/Serde.FS.SystemTextJson/Serde.FS.SystemTextJson.fsproj"
let sampleAppProj = "src/Serde.FS.SystemTextJson.SampleApp/Serde.FS.SystemTextJson.SampleApp.fsproj"
let nugetLocalDir = ".nuget-local"

// ---------------------------------------------------------------------------
// Version
// ---------------------------------------------------------------------------

let readXmlElement (projPath: string) (element: string) =
    let content = File.ReadAllText(projPath)
    let m = Regex.Match(content, $"<{element}>([^<]+)</{element}>")
    if m.Success then m.Groups.[1].Value
    else failwith $"No <{element}> found in {projPath}"

let stableVersion = readXmlElement serdeFSProj "Version"
let djinnVersion  = readXmlElement stjProj "SourceDjinnVersion"
let timestamp     = DateTime.UtcNow.ToString("yyyyMMddTHHmmss")
let debugVersion  = $"{stableVersion}.debug.{timestamp}"

// ---------------------------------------------------------------------------
// Pipeline: debug (default)
// ---------------------------------------------------------------------------

pipeline "debug" {
    description "Pack Serde packages and test the STJ backend via local NuGet feed"

    stage "Generate timestamp" {
        run (fun _ ->
            printfn $"Stable version: {stableVersion}"
            printfn $"Timestamp:      {timestamp}"
            printfn $"Debug version:  {debugVersion}"
        )
    }

    stage "Prune local feed" {
        run (fun _ ->
            if Directory.Exists(nugetLocalDir) then
                for pkg in Directory.GetFiles(nugetLocalDir, "*.nupkg", SearchOption.AllDirectories) do
                    printfn $"  Deleting {pkg}"
                    File.Delete(pkg)
            else
                Directory.CreateDirectory(nugetLocalDir) |> ignore
            printfn "Local feed pruned."
        )
    }

    stage "Pack Serde.FS" {
        run $"dotnet pack {serdeFSProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion}"
    }

    stage "Pack Serde.FS.SourceGen" {
        run $"dotnet pack {sourceGenProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
    }

    stage "Pack Serde.FS.SystemTextJson" {
        run $"dotnet pack {stjProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SourceGenVersion={debugVersion}"
    }

    stage "Restore SampleApp" {
        run $"dotnet restore {sampleAppProj} --no-cache --source .nuget-local"
    }

    stage "Build and run SampleApp" {
        run $"dotnet build {sampleAppProj} --no-restore"
        run $"dotnet run --project {sampleAppProj} --no-build"
    }

    stage "Summary" {
        run (fun _ ->
            printfn ""
            printfn "========================================"
            printfn "  Debug Pipeline Summary"
            printfn "========================================"
            printfn $"  Debug version:      {debugVersion}"
            printfn $"  Packed:"
            printfn $"    Serde.FS                  {debugVersion}"
            printfn $"    Serde.FS.SourceGen        {debugVersion}"
            printfn $"    Serde.FS.SystemTextJson   {debugVersion}"
            printfn $"  Djinn version:      {djinnVersion} (nuget.org)"
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
