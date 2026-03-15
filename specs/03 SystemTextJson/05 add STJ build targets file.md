# **Spec H — Add STJ MSBuild Targets (build + buildTransitive)**

## **Purpose**

Enable the **Serde.FS.SystemTextJson** backend to run its MSBuild‑based source generator correctly in both:

- the project that references the package, and  
- all downstream projects (via buildTransitive)

This mirrors standard MSBuild conventions for analyzer‑style generators.

---

# **Requirements**

## **1. Create `build/Serde.FS.SystemTextJson.targets`**

This file must contain **only** the `<UsingTask>` declaration.

### **Behavior**
- Makes the MSBuild task available to the project.
- Does **not** run the generator.
- Does **not** propagate transitively.

### **Content**
```xml
<Project>
  <UsingTask TaskName="Serde.FS.SourceGen.SerdeGeneratorTask"
             AssemblyFile="$(MSBuildThisFileDirectory)../analyzers/dotnet/fs/Serde.FS.SourceGen.dll" />
</Project>
```

---

## **2. Create `buildTransitive/Serde.FS.SystemTextJson.targets`**

This file contains the **SerdeGenerate** target that actually runs the generator task.

### **Behavior**
- Runs before `CoreCompile`.
- Runs in downstream projects (because it is in buildTransitive).
- Writes generated files into:  
  `$(IntermediateOutputPath)serde-stj-generated/`
- Adds generated files to compilation.

### **Content**
```xml
<Project>
  <Target Name="SerdeGenerate" BeforeTargets="CoreCompile">
    <SerdeGeneratorTask SourceFiles="@(Compile)"
                        OutputDir="$(IntermediateOutputPath)serde-stj-generated/"
                        EmitterAssemblyPath="$(MSBuildThisFileDirectory)../analyzers/dotnet/fs/Serde.FS.SystemTextJson.SourceGen.dll"
                        EmitterTypeName="StjCodeEmitter" />

    <ItemGroup>
      <Compile Include="$(IntermediateOutputPath)serde-stj-generated/**/*.fs"
               Condition="Exists('$(IntermediateOutputPath)serde-stj-generated/')" />
    </ItemGroup>
  </Target>
</Project>
```

---

# **3. Do NOT modify the JSON backend**

- JSON backend stays in `build/` only.
- JSON backend uses `serde-json-generated`.
- JSON backend does **not** need buildTransitive.

---

# **4. Do NOT change any other files**

This spec affects only:

```
Serde.FS.SystemTextJson/build/Serde.FS.SystemTextJson.targets
Serde.FS.SystemTextJson/buildTransitive/Serde.FS.SystemTextJson.targets
```

No other changes are required.

---

# **5. Acceptance Criteria**

- STJ generator runs automatically in any project that references Serde.FS.SystemTextJson.
- STJ generator also runs in downstream projects.
- Generated files appear under `obj/.../serde-stj-generated`.
- No conflicts with `serde-json-generated`.
- JSON backend remains unaffected.

---
