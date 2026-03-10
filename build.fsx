#r "nuget: Fun.Build, 1.1.17"

open System.IO
open Fun.Build

// ---------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------

let serdeFSProj       = "src/Serde.FS/Serde.FS.fsproj"
let sourceGenProj     = "src/Serde.FS.SourceGen/Serde.FS.SourceGen.fsproj"
let generatorHostProj = "src/Serde.FS.Json.GeneratorHost/Serde.FS.Json.GeneratorHost.fsproj"
let jsonProj          = "src/Serde.FS.Json/Serde.FS.Json.fsproj"
let buildDir          = ".build"

// ---------------------------------------------------------------------------
// Pipeline: build (default)
// ---------------------------------------------------------------------------

pipeline "build" {
    description "Build and pack Serde.FS, Serde.FS.SourceGen, and Serde.FS.Json"

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

    stage "Pack Serde.FS.SourceGen" {
        run $"dotnet clean {sourceGenProj}"
        run $"dotnet pack {sourceGenProj} -c Release -o {buildDir}"
    }

    stage "Pack Serde.FS" {
        run $"dotnet clean {serdeFSProj}"
        run $"dotnet pack {serdeFSProj} -c Release -o {buildDir}"
    }

    stage "Publish GeneratorHost" {
        run $"dotnet publish {generatorHostProj} -c Release"
    }

    stage "Pack Serde.FS.Json" {
        // Restore using the locally packed SourceGen
        run $"""dotnet restore {jsonProj} --source {Path.GetFullPath(buildDir)} --source "https://api.nuget.org/v3/index.json" """
        run $"dotnet clean {jsonProj}"
        run $"dotnet pack {jsonProj} -c Release -o {buildDir}"

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

tryPrintPipelineCommandHelp ()
