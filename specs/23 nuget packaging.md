## 1. Overall goals

Claude should:

1. Convert the current Serde/Djinn mono‑repo into a **four‑package ecosystem** with:
   - `FSharp.SourceDjinn` (engine, analyzer package)
   - `Serde.FS` (runtime)
   - `Serde.FS.SourceGen` (Serde generator, analyzer package)
   - `Serde.FS.Json` (backend generator, analyzer package)

2. Set up:
   - unified versioning for all **Serde** packages starting at `1.0.0-alpha.1`
   - independent SemVer for **SourceDjinn**
   - a local NuGet feed `.nuget-local/`
   - an updated `debug-build.fsx` that works via packages, not `bin/Debug`

3. Prepare the repo so `FSharp.SourceDjinn` can later be moved to its own repo without changing public behavior.

---

## 2. Versioning rules

### SourceDjinn

- **Product:** FSharp.SourceDjinn
- **PackageId:** `FSharp.SourceDjinn`
- **Version:** start at `0.1.0` (normal SemVer; Claude can parameterize later)
- Versions evolve independently of Serde.

### Serde ecosystem

All three Serde packages share the **same version number**:

- `Serde.FS`
- `Serde.FS.SourceGen`
- `Serde.FS.Json`

**Initial version for all three:**

```text
1.0.0-alpha.1
```

Later releases:

- `1.0.0-alpha.2`
- `1.0.0-alpha.3`
- …
- `1.0.0-beta.1`
- `1.0.0-rc.1`
- `1.0.0`

All three Serde packages always share the same version.

---

## 3. Package responsibilities

### 3.1 FSharp.SourceDjinn

**Project:** `FSharp.SourceDjinn`  
**Type:** analyzer NuGet package

**Requirements:**

- `PackageId`: `FSharp.SourceDjinn`
- `Version`: `0.1.0` (for now)
- Output as analyzer:

  ```xml
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>true</IncludeBuildOutput>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(OutputPath)FSharp.SourceDjinn.dll" Pack="true" PackagePath="analyzers/dotnet/fs" />
  </ItemGroup>
  ```

- Include any required engine assemblies (e.g. TypeModel, FCS) under `analyzers/dotnet/fs` as well.

---

### 3.2 Serde.FS

**Project:** `Serde.FS`  
**Type:** normal runtime library

**Requirements:**

- `PackageId`: `Serde.FS`
- `Version`: `1.0.0-alpha.1`
- No analyzer packaging.
- Contains:
  - attributes
  - runtime helpers
  - any runtime types needed by generators and users.

---

### 3.3 Serde.FS.SourceGen

**Project:** `Serde.FS.SourceGen`  
**Type:** analyzer NuGet package

**Requirements:**

- `PackageId`: `Serde.FS.SourceGen`
- `Version`: `1.0.0-alpha.1`
- Pack as analyzer:

  ```xml
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>true</IncludeBuildOutput>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(OutputPath)Serde.FS.SourceGen.dll" Pack="true" PackagePath="analyzers/dotnet/fs" />
  </ItemGroup>
  ```

- Dependencies:

  ```xml
  <ItemGroup>
    <PackageReference Include="Serde.FS" Version="1.0.0-alpha.1" />
    <PackageReference Include="FSharp.SourceDjinn" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
  ```

- No project references to Djinn or Serde.FS—only package references.

---

### 3.4 Serde.FS.Json

**Project:** `Serde.FS.Json`  
**Type:** analyzer NuGet package

**Requirements:**

- `PackageId`: `Serde.FS.Json`
- `Version`: `1.0.0-alpha.1`
- Pack as analyzer:

  ```xml
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>true</IncludeBuildOutput>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(OutputPath)Serde.FS.Json.dll" Pack="true" PackagePath="analyzers/dotnet/fs" />
  </ItemGroup>
  ```

- Dependencies:

  ```xml
  <ItemGroup>
    <PackageReference Include="Serde.FS" Version="1.0.0-alpha.1" />
    <PackageReference Include="Serde.FS.SourceGen" Version="1.0.0-alpha.1" PrivateAssets="all" />
    <PackageReference Include="FSharp.SourceDjinn" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
  ```

- No project references to other Serde projects—only package references.

---

## 4. Local NuGet feed

In the **Serde repo root**, Claude should:

1. Create a folder:

   ```text
   .nuget-local/
     FSharp.SourceDjinn/
     Serde.FS/
     Serde.FS.SourceGen/
     Serde.FS.Json/
   ```

2. Add `nuget.config` in the repo root:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <add key="local" value=".nuget-local" />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
     </packageSources>
   </configuration>
   ```

3. Ensure all Serde projects and the SampleApp restore using this `nuget.config`.

---

## 5. Debug script: debug-build.fsx

Claude should **rewrite** `debug-build.fsx` so that it:

1. **Does not** point to `src/Serde.FS.Json/bin/Debug` anymore.
2. Uses `dotnet pack` to create `.nupkg` files for:
   - `FSharp.SourceDjinn`
   - `Serde.FS`
   - `Serde.FS.SourceGen`
   - `Serde.FS.Json`
3. Writes them to the local feed:

   ```bash
   dotnet pack path/to/FSharp.SourceDjinn.fsproj \
     -c Debug \
     -o .nuget-local/FSharp.SourceDjinn \
     /p:PackageVersion=0.1.0-debug.<N>

   dotnet pack path/to/Serde.FS.fsproj \
     -c Debug \
     -o .nuget-local/Serde.FS \
     /p:PackageVersion=1.0.0-alpha.1-debug.<N>

   dotnet pack path/to/Serde.FS.SourceGen.fsproj \
     -c Debug \
     -o .nuget-local/Serde.FS.SourceGen \
     /p:PackageVersion=1.0.0-alpha.1-debug.<N>

   dotnet pack path/to/Serde.FS.Json.fsproj \
     -c Debug \
     -o .nuget-local/Serde.FS.Json \
     /p:PackageVersion=1.0.0-alpha.1-debug.<N>
   ```

   Where `<N>` is a counter the script manages (e.g. via an environment variable or a small text file).

4. Then:

   ```bash
   dotnet restore path/to/SampleApp
   dotnet build path/to/SampleApp
   dotnet run --project path/to/SampleApp
   ```

5. The script **must not** modify the `Version` in the `.fsproj` files; it should always override via `/p:PackageVersion=...` to avoid overwriting release versions.

---

## 6. Dependency behavior for users

Claude should ensure the dependency graph behaves like this:

- A user only adds:

  ```xml
  <PackageReference Include="Serde.FS.Json" Version="1.0.0-alpha.1" />
  ```

- They automatically get:
  - `Serde.FS` (runtime, visible)
  - `Serde.FS.SourceGen` (hidden via `PrivateAssets="all"`)
  - `FSharp.SourceDjinn` (hidden via `PrivateAssets="all"`)

- If later `Serde.FS.Xml` is added, it should:
  - share the same version as the other Serde packages (`1.0.0-alpha.1`, etc.)
  - depend on `Serde.FS` and `Serde.FS.SourceGen` in the same pattern
  - be an analyzer package with `analyzers/dotnet/fs` layout.

---
