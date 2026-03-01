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

let sourceDjinnProj   = "src/FSharp.SourceDjinn/FSharp.SourceDjinn.fsproj"
let serdeFSProj       = "src/Serde.FS/Serde.FS.fsproj"
let sourceGenProj     = "src/Serde.FS.SourceGen/Serde.FS.SourceGen.fsproj"
let stjProj           = "src/Serde.FS.SystemTextJson/Serde.FS.SystemTextJson.fsproj"
let sampleAppProj     = "src/Serde.FS.SystemTextJson.SampleApp/Serde.FS.SystemTextJson.SampleApp.fsproj"

let nugetLocalDir     = ".nuget-local"
let debugCounterFile  = ".debug-counter"

// Base versions (must match .fsproj Version values)
let sourceDjinnBaseVer = "0.1.0"
let serdeFSBaseVer     = "1.0.0-alpha.1"

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let bumpCounter () =
    let n =
        if File.Exists(debugCounterFile) then
            int (File.ReadAllText(debugCounterFile).Trim()) + 1
        else 1
    File.WriteAllText(debugCounterFile, string n)
    n

let updatePackageRef (projPath: string) (packageId: string) (newVersion: string) =
    let content = File.ReadAllText(projPath)
    let pattern = $"<PackageReference Include=\"{Regex.Escape(packageId)}\" Version=\"[^\"]*\""
    let replacement = $"<PackageReference Include=\"{packageId}\" Version=\"{newVersion}\""
    let updated = Regex.Replace(content, pattern, replacement)
    File.WriteAllText(projPath, updated)

let clearNuGetCache (packageId: string) =
    let cacheDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", packageId.ToLowerInvariant()
        )
    if Directory.Exists(cacheDir) then
        for dir in Directory.GetDirectories(cacheDir) do
            if Path.GetFileName(dir).Contains("-debug.") then
                printfn $"  Deleting cache: {dir}"
                Directory.Delete(dir, true)

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
    description "Pack all 4 packages and test the STJ backend via local NuGet feed"

    stage "Bump counter and clear caches" {
        run (fun _ ->
            let n = bumpCounter ()
            printfn $"Debug counter: {n}"

            for pkg in [ "fsharp.sourceDjinn"; "serde.fs"; "serde.fs.sourcegen"; "serde.fs.systemtextjson" ] do
                clearNuGetCache pkg
        )
    }

    stage "Pack FSharp.SourceDjinn" {
        run (fun _ ->
            let n = File.ReadAllText(debugCounterFile).Trim()
            let ver = $"{sourceDjinnBaseVer}-debug.{n}"
            printfn $"Packing FSharp.SourceDjinn {ver}"
            $"dotnet pack {sourceDjinnProj} -c Debug -o {nugetLocalDir}/FSharp.SourceDjinn /p:PackageVersion={ver}"
        )
    }

    stage "Pack Serde.FS" {
        run (fun _ ->
            let n = File.ReadAllText(debugCounterFile).Trim()
            let ver = $"{serdeFSBaseVer}-debug.{n}"
            printfn $"Packing Serde.FS {ver}"
            $"dotnet pack {serdeFSProj} -c Debug -o {nugetLocalDir}/Serde.FS /p:PackageVersion={ver}"
        )
    }

    stage "Pack Serde.FS.SourceGen" {
        run (fun _ ->
            let n = File.ReadAllText(debugCounterFile).Trim()
            let serdeFSVer = $"{serdeFSBaseVer}-debug.{n}"
            let djinnVer = $"{sourceDjinnBaseVer}-debug.{n}"
            let ver = $"{serdeFSBaseVer}-debug.{n}"
            printfn $"Packing Serde.FS.SourceGen {ver}"
            $"dotnet pack {sourceGenProj} -c Debug -o {nugetLocalDir}/Serde.FS.SourceGen /p:PackageVersion={ver} /p:SerdeFSVersion={serdeFSVer} /p:SourceDjinnVersion={djinnVer}"
        )
    }

    stage "Pack Serde.FS.SystemTextJson" {
        run (fun _ ->
            let n = File.ReadAllText(debugCounterFile).Trim()
            let serdeFSVer = $"{serdeFSBaseVer}-debug.{n}"
            let sourceGenVer = $"{serdeFSBaseVer}-debug.{n}"
            let djinnVer = $"{sourceDjinnBaseVer}-debug.{n}"
            let ver = $"{serdeFSBaseVer}-debug.{n}"
            printfn $"Packing Serde.FS.SystemTextJson {ver}"
            $"dotnet pack {stjProj} -c Debug -o {nugetLocalDir}/Serde.FS.SystemTextJson /p:PackageVersion={ver} /p:SerdeFSVersion={serdeFSVer} /p:SourceGenVersion={sourceGenVer} /p:SourceDjinnVersion={djinnVer}"
        )
    }

    stage "Update SampleApp and build" {
        run (fun _ ->
            let n = File.ReadAllText(debugCounterFile).Trim()
            let ver = $"{serdeFSBaseVer}-debug.{n}"
            updatePackageRef sampleAppProj "Serde.FS.SystemTextJson" ver
            printfn $"Updated SampleApp to Serde.FS.SystemTextJson {ver}"
        )
        run $"dotnet restore {sampleAppProj}"
        run $"dotnet build {sampleAppProj}"
        run $"dotnet run --project {sampleAppProj}"
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

    stage "Delete local NuGet packages" {
        run (fun _ ->
            if Directory.Exists(nugetLocalDir) then
                for subdir in Directory.GetDirectories(nugetLocalDir) do
                    for pkg in Directory.GetFiles(subdir, "*.nupkg") do
                        printfn $"  Deleting {pkg}"
                        File.Delete(pkg)
        )
    }

    stage "Delete NuGet cache entries" {
        run (fun _ ->
            for pkg in [ "fsharp.sourceDjinn"; "serde.fs"; "serde.fs.sourcegen"; "serde.fs.systemtextjson" ] do
                clearNuGetCache pkg
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
