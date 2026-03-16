# ✅ **SPEC: Remove backend control of entry point emission & make Serde.FS.SourceGen the sole authority**

## **1. Remove `EmitEntryPoint` from `ISerdeResolverEmitter`**

### **Rationale**
Entry point emission is a **project-level concern**, not a backend concern.  
Backends (JSON, STJ, future backends) must not influence whether an entry point is generated.

### **Required changes**
- Delete this property from the interface:

```fsharp
abstract member EmitEntryPoint : bool
```

- Remove all implementations of this property from:
  - `StjCodeEmitter`
  - any other backend emitters that still implement it

- Remove any logic in backend emitters that sets or uses this property.

---

## **2. Update `SerdeGeneratorEngine.generate` to compute `emitEntryPoint` backend‑agnostically**

### **Current (broken) behavior**
`emitEntryPoint` is overwritten by:

```fsharp
emitEntryPoint <- resolverEmitter.EmitEntryPoint
```

This ties entry point emission to whichever backend implements `ISerdeResolverEmitter`.  
JSON no longer implements it → entry point disappears.  
STJ still implements it → entry point depends on STJ.

### **Correct behavior**
Entry point emission must depend **only** on whether the project contains a method marked with:

```
[<FSharp.SourceDjinn.EntryPoint>]
```

### **Required change**
Replace the entire backend-dependent logic with:

```fsharp
let emitEntryPoint =
    sourceFiles
    |> Seq.exists (fun (path, text) ->
        path.EndsWith(".fs") &&
        EntryPointDetector.detect path text |> Option.isSome
    )
```

### **Then:**

```fsharp
if emitEntryPoint then
    for (filePath, sourceText) in sourceFiles do
        if filePath.EndsWith(".fs") then
            match EntryPointDetector.detect filePath sourceText with
            | Some info ->
                let code = EntryPointEmitter.emit info
                generatedSources.Add({ HintName = "~~EntryPoint.djinn.g.fs"; Code = code })
            | None -> ()
```

### **Important**
- No backend should ever influence this.
- JSON and STJ remain codec-only.
- Serde.FS.SourceGen becomes the single source of truth for entry point generation.

---

## **3. No changes required to JSON or STJ beyond removing `EmitEntryPoint`**

### JSON
Already codec-only.  
Already does not influence entry point emission.  
No further changes needed.

### STJ
Must stop influencing entry point emission.  
Removing `EmitEntryPoint` from the interface ensures this.

---

## **4. No changes required to the attribute location**

The attribute currently lives in:

```
FSharp.SourceDjinn.TypeModel
```

This is fine.  
Serde.FS.SourceGen already references Djinn at design time, so the detector/emitter work.

---

# 🎯 **Outcome**

After this spec is implemented:

### ✔ Entry point generation works again for SampleApp  
### ✔ JSON-only projects get a generated entry point  
### ✔ STJ no longer controls entry point emission  
### ✔ Backends cannot conflict  
### ✔ Serde.FS.SourceGen becomes the unified entry point generator  
### ✔ The architecture is clean, stable, and future-proof  

---
