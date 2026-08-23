# Serde.FS

## Releasing (NuGet Trusted Publishing — keyless, no API keys)

1. Bump `<SerdeVersion>` in the root `Directory.Build.props` (single suite version for all four packages).
2. Commit and push to `main`.
3. GitHub → Actions → "Publish to NuGet" → Run workflow (or `gh workflow run publish.yml`). It tests both suites in Release, packs via `build.fsx`, and pushes `.build/*.nupkg` using a 1-hour OIDC-minted key — authorized by the Trusted Publishing policy on nuget.org (scoped to this repo + the four package IDs).
4. Tag the released commit: `git tag v<version> && git push origin v<version>`.

`build.fsx` remains the local pack (`dotnet fsi build.fsx` → `.build/`); publishing is CI-only.

## Local debug build

* Run `dotnet fsi debug-build.fsx` to build, pack, and test the Json backend against the SampleApp.
* Run `dotnet fsi debug-build.fsx -- -p clean` to delete debug packages and SampleApp build artifacts.
* Run `dotnet fsi debug-build.fsx -- -p aot` (after a `debug` run) to publish the SampleApp trimmed + self-contained and run it — the Native AOT / trimming regression gate for the codec pipeline (issue #14). A real `PublishAot=true` publish additionally needs the VS C++ toolchain.

* The pipeline uses timestamped debug versions (e.g., `1.0.0-alpha.1.debug.20260228T120000`) derived from the stable version in `.fsproj` files.
* FSharp.SourceDjinn is folded into this repo as in-tree projects (`src/FSharp.SourceDjinn/`, `src/FSharp.SourceDjinn.TypeModel/`), consumed via `ProjectReference` with `PrivateAssets="all"` — it never appears in consumer nuspecs. Rationale lives in the comment block in `Serde.FS.SourceGen.fsproj`.
* The SampleApp `.fsproj` is never modified. The restore uses `--source .nuget-local` with `--no-cache` to resolve the debug version.
* The `.nuget-local/` feed is pruned at the start of each debug run.
* A summary block is printed at the end of the pipeline.
* Use `-- -p <name>` to select a pipeline (e.g., `-- -p clean`).

## Skills

* **Add a codec factory** (`.claude/skills/add-codec-factory.md`): Follow this when adding runtime codec support for a new built-in type (e.g., `Dictionary`, `HashSet`). Covers the factory implementation, registry registration, tests, and SampleRpc integration.
