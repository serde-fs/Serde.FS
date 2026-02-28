#r "nuget: Fun.Build, 1.1.17"

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open Fun.Build

// Usage:
//   dotnet fsi debug-systemtextjson.fsx              runs the "debug" pipeline (default)
//   dotnet fsi debug-systemtextjson.fsx -- -p clean  runs the "clean" pipeline

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let sourceGenProj = "src/FSharp.SourceDjinn/FSharp.SourceDjinn.fsproj"
let stjProj       = "src/Serde.FS.SystemTextJson/Serde.FS.SystemTextJson.fsproj"
let sampleAppProj = "src/Serde.FS.SystemTextJson.SampleApp/Serde.FS.SystemTextJson.SampleApp.fsproj"

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let readVersion (projPath: string) =
    let content = File.ReadAllText(projPath)
    let m = Regex.Match(content, @"<Version>([^<]+)</Version>")
    if m.Success then m.Groups.[1].Value
    else failwith $"Could not find <Version> in {projPath}"

let nextDebugVersion (version: string) =
    let baseVersion = version.Split('-').[0]
    let m = Regex.Match(version, @"-debug\.(\d+)$")
    if m.Success then
        $"{baseVersion}-debug.{int m.Groups.[1].Value + 1}"
    else
        $"{baseVersion}-debug.1"

let writeVersion (projPath: string) (newVersion: string) =
    let content = File.ReadAllText(projPath)
    let updated = Regex.Replace(content, @"<Version>[^<]+</Version>", $"<Version>{newVersion}</Version>")
    File.WriteAllText(projPath, updated)

let updatePackageRef (projPath: string) (packageId: string) (newVersion: string) =
    let content = File.ReadAllText(projPath)
    let pattern = $"<PackageReference Include=\"{Regex.Escape(packageId)}\" Version=\"[^\"]*\""
    let replacement = $"<PackageReference Include=\"{packageId}\" Version=\"{newVersion}\""
    let updated = Regex.Replace(content, pattern, replacement)
    File.WriteAllText(projPath, updated)

let killProcess (name: string) =
    try
        let psi = ProcessStartInfo("taskkill", $"/F /IM {name}")
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.CreateNoWindow <- true
        use p = Process.Start(psi)
        p.WaitForExit()
        if p.ExitCode = 0 then printfn $"  Killed {name}"
    with _ -> ()

// ---------------------------------------------------------------------------
// Pipeline: debug (default)
// ---------------------------------------------------------------------------

pipeline "debug" {
    description "Build, pack, and test the STJ backend using a local NuGet feed"

    stage "Bump debug version" {
        run (fun _ ->
            let version = readVersion stjProj
            let newVersion = nextDebugVersion version
            printfn $"Version: {version} -> {newVersion}"
            writeVersion stjProj newVersion
        )
    }

    stage "Build SourceGen" {
        run $"dotnet build {sourceGenProj}"
    }

    stage "Build STJ" {
        run $"dotnet build {stjProj}"
    }

    stage "Update SampleApp package reference" {
        run (fun _ ->
            let version = readVersion stjProj
            updatePackageRef sampleAppProj "Serde.FS.SystemTextJson" version
            printfn $"Updated SampleApp to Serde.FS.SystemTextJson {version}"
        )
    }

    stage "Restore and build SampleApp" {
        run $"dotnet restore {sampleAppProj}"
        run $"dotnet build {sampleAppProj}"
    }

    runIfOnlySpecified false
}

// ---------------------------------------------------------------------------
// Pipeline: clean
// ---------------------------------------------------------------------------

pipeline "clean" {
    description "Kill build processes, delete debug packages and caches"

    // File cleanup runs before process killing because taskkill /F /IM dotnet.exe
    // will terminate this script's host process.

    stage "Delete debug packages" {
        run (fun _ ->
            let dir = "src/Serde.FS.SystemTextJson/bin/Debug"
            if Directory.Exists(dir) then
                for pkg in Directory.GetFiles(dir, "*-debug.*.nupkg") do
                    printfn $"  Deleting {pkg}"
                    File.Delete(pkg)
        )
    }

    stage "Delete NuGet cache entries" {
        run (fun _ ->
            let cacheDir =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages", "serde.fs.systemtextjson"
                )
            if Directory.Exists(cacheDir) then
                for dir in Directory.GetDirectories(cacheDir) do
                    if Path.GetFileName(dir).Contains("-debug.") then
                        printfn $"  Deleting {dir}"
                        Directory.Delete(dir, true)
        )
    }

    stage "Delete SampleApp obj/bin" {
        run (fun _ ->
            for dir in [ "src/Serde.FS.SystemTextJson.SampleApp/obj"; "src/Serde.FS.SystemTextJson.SampleApp/bin" ] do
                if Directory.Exists(dir) then
                    printfn $"  Deleting {dir}"
                    Directory.Delete(dir, true)
        )
    }

    stage "Kill build processes" {
        run (fun _ ->
            for proc in [
                "MSBuild.exe"
                "VBCSCompiler.exe"
                "ServiceHub.RoslynCodeAnalysisService.exe"
                "ServiceHub.Host.CLR.exe"
                "ServiceHub.Host.CLR.x86.exe"
                "dotnet.exe" // Last: this kills the script's own host process
            ] do
                killProcess proc
        )
    }

    runIfOnlySpecified
}

tryPrintPipelineCommandHelp ()
