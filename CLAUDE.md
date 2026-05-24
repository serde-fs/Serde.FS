# Serde.FS

## Local debug build

* Run `dotnet fsi debug-build.fsx` to build, pack, and test the Json backend against the SampleApp.
* Run `dotnet fsi debug-build.fsx -- -p clean` to delete debug packages and SampleApp build artifacts.

* The pipeline uses timestamped debug versions (e.g., `1.0.0-alpha.1.debug.20260228T120000`) derived from the stable version in `.fsproj` files.
* FSharp.SourceDjinn is folded into this repo as in-tree projects (`src/FSharp.SourceDjinn/`, `src/FSharp.SourceDjinn.TypeModel/`), consumed via `ProjectReference` with `PrivateAssets="all"` — it never appears in consumer nuspecs. Rationale lives in the comment block in `Serde.FS.SourceGen.fsproj`.
* The SampleApp `.fsproj` is never modified. The restore uses `--source .nuget-local` with `--no-cache` to resolve the debug version.
* The `.nuget-local/` feed is pruned at the start of each debug run.
* A summary block is printed at the end of the pipeline.
* Use `-- -p <name>` to select a pipeline (e.g., `-- -p clean`).

## Skills

* **Add a codec factory** (`.claude/skills/add-codec-factory.md`): Follow this when adding runtime codec support for a new built-in type (e.g., `Dictionary`, `HashSet`). Covers the factory implementation, registry registration, tests, and SampleRpc integration.
