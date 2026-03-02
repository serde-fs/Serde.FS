# **Spec: Debug Pipeline for Serde.FS (Json)**

## **Purpose**
Implement a Fun.Build‑based debug pipeline (`debug-build.fsx`) that builds, packs, and tests the Serde.FS Json backend using a **timestamped debug version** and a **clean, isolated local NuGet feed**. This pipeline is the primary local development loop.

The pipeline must be deterministic, isolated, and must not modify project files.

---

## **High‑Level Behavior**
The debug pipeline must:

1. Generate a **timestamped debug version** based on the stable version in the `.fsproj` files.
2. **Prune** the `.nuget-local/` directory of all Serde packages before packing.
3. Pack the three Serde packages:
   - `Serde.FS`
   - `Serde.FS.SourceGen`
   - `Serde.FS.Json`
4. Apply the timestamped version using `/p:PackageVersion=…` overrides.
5. **Do not pack Djinn.** Djinn is consumed from nuget.org as a stable dependency.
6. Restore the SampleApp using:
   - `--no-cache`
   - `--source .nuget-local`
   - `--prerelease`
7. Build and run the SampleApp.
8. Print a **single summary block** at the end of the pipeline.

The pipeline must never modify project files.

---

## **Invariants**
Claude must preserve these invariants:

### **Versioning**
- All Serde `.fsproj` files keep a stable version (e.g., `1.0.0-alpha.1`).
- Debug builds use a timestamped version appended to the stable version.
- No counters.
- No editing `.fsproj` files.
- No updating SampleApp package references.

### **Local Feed**
- `.nuget-local/` is pruned at the start of each debug run.
- Only freshly packed Serde packages exist in the feed.
- Djinn is **not** placed in the local feed.

### **Restore Behavior**
- SampleApp restore must use:
  - `--no-cache`
  - `--source .nuget-local`
  - `--prerelease`
- Restore must always select the newest timestamped debug version.
- SampleApp `.fsproj` must never be modified.

### **Djinn**
- Djinn is consumed from nuget.org at a stable version (e.g., `0.1.0`).
- Djinn is never packed locally.
- No version overrides for Djinn.

### **Summary Block**
At the end of the pipeline, print a summary containing:

- The timestamped debug version used.
- The Serde packages packed and their versions.
- The Djinn version resolved (from nuget.org).
- The restore source and cache mode.
- The SampleApp version resolved at restore time.

---

## **Pipeline Structure**
Implement two pipelines:

### **Pipeline: `debug` (default)**
Stages:

1. **Generate timestamp**
   - Create a timestamp string.
   - Construct the debug version.

2. **Prune local feed**
   - Delete all `.nupkg` files under `.nuget-local/` except directory structure.

3. **Pack Serde packages**
   - Pack `Serde.FS`
   - Pack `Serde.FS.SourceGen`
   - Pack `Serde.FS.Json`
   - All using the same timestamped version override.

4. **Restore SampleApp (isolated)**
   - Use `--no-cache`
   - Use `--source .nuget-local`
   - Use `--prerelease`

5. **Build + run SampleApp**

6. **Print summary block**

### **Pipeline: `clean`**
Stages:

1. Delete all `.nupkg` files under `.nuget-local/`.
2. Delete SampleApp `obj/` and `bin/` directories.

No process killing.  
No global cache deletion.

---

## **What Claude Must Not Do**
- Must not modify any `.fsproj` file.
- Must not introduce counters.
- Must not pack Djinn.
- Must not clear the global NuGet cache.
- Must not kill processes.
- Must not update SampleApp package references.
- Must not add version‑rewriting logic.
- Must not add global state.

---

## **Integration with CLAUDE.md**
Claude.md should contain a short behavioral description:

- How to run the debug pipeline.
- That it uses timestamped debug versions.
- That Djinn comes from nuget.org.
- That SampleApp `.fsproj` is never modified.
- That the pipeline prunes `.nuget-local/` and restores with `--no-cache`.
- That a summary block is printed at the end.
- That `-p` selects pipelines.

No implementation details belong in CLAUDE.md.

---

## **Deliverables**
Claude must produce:

- A complete `debug-build.fsx` implementing this spec.
- Clean, intention‑revealing Fun.Build stages.
- A summary block printed at the end.
- A simplified `clean` pipeline.
- No counter file.
- No version rewriting.
- No Djinn packing.

---
