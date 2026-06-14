#r "nuget: Fun.Build, 1.1.17"

open System.IO
open Fun.Build

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let serdeFSProj            = "src/Serde.FS/Serde.FS.fsproj"
let generatorHostProj      = "src/Serde.FS.Json.GeneratorHost/Serde.FS.Json.GeneratorHost.fsproj"
let fableGeneratorHostProj = "src/Serde.FS.Fable.GeneratorHost/Serde.FS.Fable.GeneratorHost.fsproj"
let jsonProj               = "src/Serde.FS.Json/Serde.FS.Json.fsproj"
let aspNetProj             = "src/Serde.FS.AspNet/Serde.FS.AspNet.fsproj"
let fableProj              = "src/Serde.FS.Fable/Serde.FS.Fable.fsproj"
let buildDir               = ".build"

// ---------------------------------------------------------------------------
// Pipeline: build (default)
// ---------------------------------------------------------------------------

pipeline "build" {
    description "Build and pack Serde.FS, Serde.FS.Json, Serde.FS.AspNet, and Serde.FS.Fable"

    stage "Prepare output directory" {
        run (fun _ ->
            if Directory.Exists(buildDir) then
                for pkg in Directory.GetFiles(buildDir, "*.nupkg", SearchOption.AllDirectories) do
                    File.Delete(pkg)
            else
                Directory.CreateDirectory(buildDir) |> ignore

            printfn $"Output directory: {buildDir}"
        )
    }

    stage "Pack Serde.FS" {
        run $"dotnet clean {serdeFSProj}"
        run $"dotnet pack {serdeFSProj} -c Release -o {buildDir}"
    }

    stage "Publish GeneratorHosts" {
        run $"dotnet publish {generatorHostProj} -c Release"
        run $"dotnet publish {fableGeneratorHostProj} -c Release"
    }

    stage "Pack Serde.FS.Json" {
        // Restore using the locally packed Serde.FS runtime dep
        run $"""dotnet restore {jsonProj} --source {Path.GetFullPath(buildDir)} --source "https://api.nuget.org/v3/index.json" """
        run $"dotnet clean {jsonProj}"
        run $"dotnet pack {jsonProj} -c Release -o {buildDir}"
    }

    stage "Pack Serde.FS.AspNet" {
        run $"dotnet clean {aspNetProj}"
        run $"dotnet pack {aspNetProj} -c Release -o {buildDir}"
    }

    stage "Pack Serde.FS.Fable" {
        // Restore using the locally packed Serde.FS (the runtime dep — the
        // Fable generator host's publish output was produced above).
        run $"""dotnet restore {fableProj} --source {Path.GetFullPath(buildDir)} --source "https://api.nuget.org/v3/index.json" """
        run $"dotnet clean {fableProj}"
        run $"dotnet pack {fableProj} -c Release -o {buildDir}"
    }

    stage "Summary" {
        run (fun _ ->
            printfn ""
            printfn "========================================"
            printfn "  Build Summary"
            printfn "========================================"
            printfn $"  Output:   {buildDir}/"
            printfn $"  Packages:"
            if Directory.Exists(buildDir) then
                for pkg in Directory.GetFiles(buildDir, "*.nupkg") do
                    printfn $"    {Path.GetFileName(pkg)}"
            printfn "========================================"
            printfn ""
        )
    }

    runIfOnlySpecified false
}

// ---------------------------------------------------------------------------
// Pipeline: push
// ---------------------------------------------------------------------------

pipeline "push" {
    description "Push all .nupkg files in .build/ to NuGet.org (requires SERDE_FS_NUGET_KEY env var)"

    stage "push" {
        whenEnvVar "SERDE_FS_NUGET_KEY"
        run (fun ctx ->
            let key = ctx.GetEnvVar "SERDE_FS_NUGET_KEY"
            if not (Directory.Exists(buildDir)) then
                failwith $"No '{buildDir}' directory found. Run the 'build' pipeline first."
            let packages = Directory.GetFiles(buildDir, "*.nupkg")
            if packages.Length = 0 then
                failwith $"No .nupkg files found in '{buildDir}/'. Run the 'build' pipeline first."
            for pkg in packages do
                printfn $"  Pushing {Path.GetFileName(pkg)}..."
                ctx.RunSensitiveCommand $"""dotnet nuget push {pkg} -s https://api.nuget.org/v3/index.json -k {key}"""
                |> Async.RunSynchronously
                |> ignore
        )
    }

    runIfOnlySpecified
}

tryPrintPipelineCommandHelp ()
