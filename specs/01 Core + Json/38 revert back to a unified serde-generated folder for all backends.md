# ✅ **SPEC: Unify all Serde-generated output into a single `serde-generated` folder**

## **Goal**
Replace backend-specific output directories:

- `serde-json-generated`
- `serde-stj-generated`
- `serde-djinn-generated` (implicit)
- any other backend-specific folders

with a single unified folder:

```
obj/serde-generated/
```

All generated files will continue to use backend-specific suffixes:

- `*.json.g.fs`
- `*.stj.g.fs`
- `*.djinn.g.fs`

This ensures no filename collisions while eliminating folder ambiguity.

---

# 1. **Update both GeneratorHost projects**

There are two GeneratorHost projects:

- `Serde.FS.SourceGen.GeneratorHost`
- `Serde.FS.SystemTextJson.GeneratorHost` (if separate)
- (If JSON has its own host, update that too)

### **Required changes**

#### **1.1. Update stale file cleanup**
Replace any cleanup logic that targets backend-specific folders with:

```fsharp
let generatedFolder = Path.Combine(projectDirectory, "obj", "serde-generated")
```

Cleanup should delete:

- `*.json.g.fs`
- `*.stj.g.fs`
- `*.djinn.g.fs`

Example pattern:

```fsharp
Directory.EnumerateFiles(generatedFolder, "*.json.g.fs")
Directory.EnumerateFiles(generatedFolder, "*.stj.g.fs")
Directory.EnumerateFiles(generatedFolder, "*.djinn.g.fs")
```

#### **1.2. Update source file exclusion**
Where the host excludes generated files from compilation, replace backend-specific folder paths with:

```
obj/serde-generated/**/*.g.fs
```

---

# 2. **Update all .targets files**

There are three .targets files:

- `Serde.FS.Json.targets`
- `Serde.FS.SystemTextJson.targets`
- `Serde.FS.SourceGen.targets`

### **Required changes**

#### **2.1. Replace backend-specific output folder properties**

Wherever you see:

```
$(IntermediateOutputPath)serde-json-generated\
$(IntermediateOutputPath)serde-stj-generated\
$(IntermediateOutputPath)serde-djinn-generated\
```

Replace with:

```
$(IntermediateOutputPath)serde-generated\
```

#### **2.2. Update `<Compile Include=...>` paths**

Replace:

```
<Compile Include="$(IntermediateOutputPath)serde-json-generated\**\*.fs" />
```

with:

```
<Compile Include="$(IntermediateOutputPath)serde-generated\**\*.fs" />
```

Same for STJ and Djinn.

#### **2.3. Update cleanup targets**

Any `<RemoveDir>` or `<Delete>` tasks referencing backend-specific folders must be updated to:

```
<RemoveDir Directories="$(IntermediateOutputPath)serde-generated" />
```

---

# 3. **Update all emitters to write into the unified folder**

### **3.1. JSON emitter**
Currently writes to:

```
serde-json-generated
```

Update to:

```
serde-generated
```

### **3.2. STJ emitter**
Currently writes to:

```
serde-stj-generated
```

Update to:

```
serde-generated
```

### **3.3. EntryPointEmitter**
Currently writes into whichever backend folder is active.

Update to:

```
serde-generated
```

---

# 4. **No changes to file naming conventions**

All generated files must retain their backend-specific suffixes:

- JSON: `*.json.g.fs`
- STJ: `*.stj.g.fs`
- Djinn entry point: `*.djinn.g.fs`

This ensures:

- No collisions
- No ambiguity
- No need for backend-specific folders

---

# 5. **Validation criteria**

After the change:

### ✔ SampleApp with only JSON backend  
Produces:

```
obj/serde-generated/~SerdeJsonCodecs.json.g.fs
obj/serde-generated/~~EntryPoint.djinn.g.fs
```

### ✔ SampleApp with JSON + STJ  
Produces:

```
obj/serde-generated/~SerdeJsonCodecs.json.g.fs
obj/serde-generated/~SerdeStjResolver.stj.g.fs
obj/serde-generated/~~EntryPoint.djinn.g.fs
```

### ✔ No backend-specific folders exist  
`serde-json-generated` and `serde-stj-generated` must not appear.

### ✔ No stale files remain  
Cleanup removes all `*.json.g.fs`, `*.stj.g.fs`, `*.djinn.g.fs` from the unified folder.

---

# 6. **Non-goals (explicitly)**

Claude should NOT:

- Change file naming conventions  
- Change backend behavior  
- Change resolver logic  
- Change entry point detection  
- Change codec generation  
- Introduce new folders  
- Modify MSBuild outside the .targets files  

This spec is **only** about folder unification.

---
