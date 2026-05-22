#r "nuget: Fun.Build, 1.1.17"

open System
open System.IO
open Fun.Build

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let serdeFSProj       = "src/Serde.FS/Serde.FS.fsproj"
let generatorHostProj    = "src/Serde.FS.Json.GeneratorHost/Serde.FS.Json.GeneratorHost.fsproj"
let stjGeneratorHostProj = "src/Serde.FS.SystemTextJson.GeneratorHost/Serde.FS.SystemTextJson.GeneratorHost.fsproj"
let fableGeneratorHostProj = "src/Serde.FS.Json.Fable.GeneratorHost/Serde.FS.Json.Fable.GeneratorHost.fsproj"
let stjProj           = "src/Serde.FS.Json/Serde.FS.Json.fsproj"
let stjSystemTextJsonProj = "src/Serde.FS.SystemTextJson/Serde.FS.SystemTextJson.fsproj"
let fableProj         = "src/Serde.FS.Json.Fable/Serde.FS.Json.Fable.fsproj"
let sampleRpcSharedProj = "src/Serde.FS.Json.SampleRpc.Shared/Serde.FS.Json.SampleRpc.Shared.fsproj"
let sampleRpcServerProj = "src/Serde.FS.Json.SampleRpc.Server/Serde.FS.Json.SampleRpc.Server.fsproj"
let sampleRpcClientProj = "src/Serde.FS.Json.SampleRpc.Client/Serde.FS.Json.SampleRpc.Client.fsproj"
let sampleRpcFableClientProj = "src/Serde.FS.Json.SampleRpc.FableClient/Serde.FS.Json.SampleRpc.FableClient.fsproj"
let sampleAppProj     = "src/Serde.FS.Json.SampleApp/Serde.FS.Json.SampleApp.fsproj"
let sourceGenTestProj = "src/Serde.FS.SourceGen.Tests/Serde.FS.SourceGen.Tests.fsproj"
let jsonTestProj      = "src/Serde.FS.Json.Tests/Serde.FS.Json.Tests.fsproj"
let nugetLocalDir     = ".nuget-local"

// ---------------------------------------------------------------------------
// Version helpers (read from Directory.Build.props)
// ---------------------------------------------------------------------------

let readProp (propName: string) =
    let content = File.ReadAllText("Directory.Build.props")
    let tag = $"<{propName}>"
    let idx = content.IndexOf(tag)
    if idx = -1 then failwith $"No <{propName}> found in Directory.Build.props"
    let start = idx + tag.Length
    let endIdx = content.IndexOf($"</{propName}>", start)
    content.Substring(start, endIdx - start).Trim()

let stableSerdeFSVersion     = readProp "SerdeFSVersion"
let stableSerdeJsonVersion   = readProp "SerdeJsonVersion"
let stableSerdeFableVersion  = readProp "SerdeFableVersion"
let stableSerdeStjVersion    = readProp "SerdeStjVersion"

let timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss")

// All debug packages share the same version
let debugVersion = $"{stableSerdeFSVersion}.debug.{timestamp}"

// Helper to write Directory.Build.props for a project
let writeVersionProps (projPath: string) (props: (string * string) list) =
    let propsPath = Path.Combine(Path.GetDirectoryName(projPath), "Directory.Build.props")
    let propLines = props |> List.map (fun (k, v) -> $"    <{k}>{v}</{k}>") |> String.concat "\n"
    let content = $"""<Project>
  <PropertyGroup>
{propLines}
  </PropertyGroup>
</Project>
"""
    File.WriteAllText(propsPath, content)
    printfn $"  Wrote {propsPath}"

// ---------------------------------------------------------------------------
// Pipeline: debug (default)
// ---------------------------------------------------------------------------

pipeline "debug" {
    description "Pack Serde packages and test via local NuGet feed"

    stage "Show versions" {
        run (fun _ ->
            printfn $"Stable Serde.FS:       {stableSerdeFSVersion}"
            printfn $"Stable Serde.FS.Json:  {stableSerdeJsonVersion}"
            printfn $"Stable Serde.FS.Fable: {stableSerdeFableVersion}"
            printfn $"Stable Serde.FS.STJ:   {stableSerdeStjVersion}"
            printfn $"Timestamp:             {timestamp}"
            printfn $"Debug version:         {debugVersion}"
        )
    }

    stage "Prune local feed and global cache" {
        run (fun _ ->
            if Directory.Exists(nugetLocalDir) then
                for pkg in Directory.GetFiles(nugetLocalDir, "*.nupkg", SearchOption.AllDirectories) do
                    let name = Path.GetFileName(pkg)
                    if name.StartsWith("Serde.", StringComparison.OrdinalIgnoreCase) then
                        printfn $"  Deleting {pkg}"
                        File.Delete(pkg)
                    else
                        printfn $"  Keeping  {pkg}"
            else
                Directory.CreateDirectory(nugetLocalDir) |> ignore

            printfn "Local feed pruned."

            let globalPkgs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            for pkgName in [ "serde.fs"; "serde.fs.sourcegen"; "serde.fs.json"; "serde.fs.json.fable"; "serde.fs.systemtextjson" ] do
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

    stage "Pack Serde.FS" {
        run $"dotnet clean {serdeFSProj}"
        run $"dotnet build {serdeFSProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
        run $"dotnet pack {serdeFSProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
    }

    stage "Publish GeneratorHosts" {
        run $"dotnet publish {generatorHostProj} -c Debug"
        run $"dotnet publish {stjGeneratorHostProj} -c Debug"
        run $"dotnet publish {fableGeneratorHostProj} -c Debug"
    }

    stage "Pack Serde.FS.Json" {
        run $"dotnet restore {stjProj} --no-cache"
        run $"dotnet clean {stjProj}"
        run $"dotnet build {stjProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
        run $"dotnet pack {stjProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion}"
    }

    stage "Pack Serde.FS.Json.Fable" {
        run $"dotnet restore {fableProj} --no-cache /p:SerdeFSVersion={debugVersion}"
        run $"dotnet clean {fableProj}"
        run $"dotnet build {fableProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SerdeFableVersion={debugVersion}"
        run $"dotnet pack {fableProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SerdeFableVersion={debugVersion}"
    }

    stage "Pack Serde.FS.SystemTextJson" {
        run $"dotnet restore {stjSystemTextJsonProj} --no-cache /p:SerdeFSVersion={debugVersion}"
        run $"dotnet clean {stjSystemTextJsonProj}"
        run $"dotnet build {stjSystemTextJsonProj} -c Debug /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SerdeStjVersion={debugVersion}"
        run $"dotnet pack {stjSystemTextJsonProj} -c Debug -o {nugetLocalDir} /p:PackageVersion={debugVersion} /p:SerdeFSVersion={debugVersion} /p:SerdeStjVersion={debugVersion}"
    }

    stage "Run tests" {
        run $"dotnet test {sourceGenTestProj} -c Debug --no-restore"
        run $"dotnet test {jsonTestProj} -c Debug --no-restore"
    }

    stage "Write version props" {
        run (fun _ ->
            writeVersionProps sampleRpcSharedProj [ "SerdeFSVersion", debugVersion ]
            writeVersionProps sampleRpcServerProj [ "SerdeJsonVersion", debugVersion ]
            writeVersionProps sampleRpcClientProj [ "SerdeJsonVersion", debugVersion ]
            writeVersionProps sampleRpcFableClientProj [ "SerdeFableVersion", debugVersion ]
            writeVersionProps sampleAppProj [ "SerdeJsonVersion", debugVersion; "SerdeStjVersion", debugVersion ]
        )
    }

    stage "Restore sample projects" {
        run $"dotnet restore {sampleRpcSharedProj} --no-cache"
        run $"dotnet restore {sampleRpcServerProj} --no-cache"
        run $"dotnet restore {sampleRpcClientProj} --no-cache"
        run $"dotnet restore {sampleRpcFableClientProj} --no-cache"
        run $"dotnet restore {sampleAppProj} --no-cache"
    }

    stage "Build and run SampleApp" {
        run $"dotnet build {sampleAppProj} --no-restore"
        run $"dotnet run --project {sampleAppProj} --no-build"
    }

    stage "Build SampleRpc.Server" {
        run $"dotnet build {sampleRpcServerProj} --no-restore"
    }

    stage "Build SampleRpc.Client" {
        run $"dotnet build {sampleRpcClientProj} --no-restore"
    }

    stage "Build SampleRpc.FableClient" {
        // Verifies the new Serde.FS.Json.Fable consumer flow: installing the
        // package on a Fable client project triggers generation of
        // ~<Api>.fable.g.fs into the project's OWN fable-generated/ folder
        // during its build (no cross-project writes). We use --no-restore
        // because the prior Restore stage handled it.
        run $"dotnet build {sampleRpcFableClientProj} --no-restore"
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
            printfn $"    Serde.FS.Json             {debugVersion}"
            printfn $"    Serde.FS.Json.Fable       {debugVersion}"
            printfn $"    Serde.FS.SystemTextJson   {debugVersion}"
            printfn $"  Sample projects:"
            printfn $"    SampleApp                 OK"
            printfn $"    SampleRpc.Server          OK"
            printfn $"    SampleRpc.Client          OK"
            printfn $"    SampleRpc.FableClient     OK"
            printfn "========================================"
            printfn ""
        )
    }

    runIfOnlySpecified false
}

tryPrintPipelineCommandHelp ()
