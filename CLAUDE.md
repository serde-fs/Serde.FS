# Serde.FS

## Local debug build

* Run `dotnet fsi debug-systemtextjson.fsx` to build, pack, and test the SystemTextJson backend against the SampleApp.
* Run `dotnet fsi debug-systemtextjson.fsx -- -p clean` to delete debug packages and SampleApp build artifacts.

* The pipeline uses timestamped debug versions (e.g., `1.0.0-alpha.1.debug.20260228T120000`) derived from the stable version in `.fsproj` files.
* FSharp.SourceDjinn is consumed from nuget.org at its stable version — it is never packed locally.
* The SampleApp `.fsproj` is never modified. The restore uses `--source .nuget-local` with `--no-cache` to resolve the debug version.
* The `.nuget-local/` feed is pruned at the start of each debug run.
* A summary block is printed at the end of the pipeline.
* Use `-- -p <name>` to select a pipeline (e.g., `-- -p clean`).
