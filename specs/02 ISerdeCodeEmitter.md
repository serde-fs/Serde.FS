# ⭐ **Claude Spec — Invert Codegen + Add Debug Backend**

This spec tells Claude exactly what to do, in the right order, with the right constraints, without letting him wander.

---

# 1. **Move metadata types into Serde.FS**

### **Goal:**  
Make the metadata model backend‑agnostic and accessible to both SourceGen and all backends.

### **Actions:**
1. In `Serde.FS`, create a new file (or reuse an existing one) named `SerdeMetadata.fs`.
2. Move the following types from `Serde.FS.SourceGen` into this file:
   - `SerdeTypeInfo`
   - `SerdeFieldInfo`
   - Any other pure metadata types used by the generator.

### **Constraints:**
- These types must contain **no STJ references**.
- They must describe F# shapes only (records, DUs, fields, attributes, etc.).
- Namespace should be `Serde.FS`.

---

# 2. **Introduce ISerdeCodeEmitter in Serde.FS**

### **Goal:**  
Define the backend interface that SourceGen will call.

### **Add this interface:**

```fsharp
namespace Serde.FS

type ISerdeCodeEmitter =
    abstract member Emit : SerdeTypeInfo -> string
```

### **Add a simple registry:**

```fsharp
module SerdeCodegenRegistry =
    let mutable private defaultEmitter : ISerdeCodeEmitter option = None

    let setDefaultEmitter emitter =
        defaultEmitter <- Some emitter

    let getDefaultEmitter () =
        defaultEmitter
```

### **Constraints:**
- No STJ references.
- No backend-specific logic.
- This is the only contract between SourceGen and backends.

---

# 3. **Refactor Serde.FS.SourceGen to delegate codegen**

### **Goal:**  
SourceGen should only:
- analyze types  
- produce `SerdeTypeInfo`  
- call the backend emitter  

### **Actions:**
1. Remove all STJ-specific code from `CodeEmitter.fs`.
2. Replace it with a thin adapter:

```fsharp
let emit (info: SerdeTypeInfo) =
    match SerdeCodegenRegistry.getDefaultEmitter() with
    | Some emitter -> emitter.Emit(info)
    | None -> failwith "No Serde code emitter registered. Call SerdeJson.useAsDefault() or register a backend."
```

3. Ensure SourceGen no longer references:
   - `System.Text.Json`
   - `JsonTypeInfo`
   - `JsonMetadataServices`
   - Any STJ types

### **Constraints:**
- SourceGen must be backend-agnostic.
- SourceGen must not import STJ namespaces.

---

# 4. **Move STJ-specific codegen into Serde.FS.STJ**

### **Goal:**  
STJ becomes a backend that implements `ISerdeCodeEmitter`.

### **Actions:**
1. Create a new file in STJ: `JsonCodeEmitter.fs`.
2. Move the entire STJ-specific codegen logic from SourceGen into:

```fsharp
type JsonCodeEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) =
            // existing STJ codegen logic goes here
```

3. Update `SerdeJson.useAsDefault()` to register the emitter:

```fsharp
let useAsDefault () =
    SerdeCodegenRegistry.setDefaultEmitter (JsonCodeEmitter())
    // existing backend registration logic stays
```

### **Constraints:**
- STJ is now the only place that references STJ APIs.
- No STJ code remains in SourceGen.

---

# 5. **Add a Debug backend to validate the architecture**

### **Goal:**  
Provide a trivial backend that proves the inversion works and is easy to test.

### **Actions:**
1. In `Serde.FS` (or a new project `Serde.FS.Debug`), create:

```fsharp
module Serde.FS.Debug

open Serde.FS

type DebugEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) =
            sprintf "// DEBUG EMIT: %s" info.TypeName
```

2. Add a registration helper:

```fsharp
module SerdeDebug =
    let useAsDefault () =
        SerdeCodegenRegistry.setDefaultEmitter (DebugEmitter())
```

### **Expected behavior:**
If a user calls:

```fsharp
SerdeDebug.useAsDefault()
```

Then generated files should contain:

```fsharp
// DEBUG EMIT: Person
```

This proves:
- the registry works  
- SourceGen delegates correctly  
- backends are pluggable  
- the architecture is inverted cleanly  

---

# 6. **Update tests**

### **SourceGen tests:**
- Should test only type analysis.
- Should use a fake emitter to assert correct metadata is passed.

### **STJ tests:**
- Should test STJ codegen output.

### **Debug backend tests:**
- Should verify that `DebugEmitter.Emit` returns the expected string.

---

# 7. **Acceptance Criteria**

- `SerdeTypeInfo` lives in Serde.FS.
- `ISerdeCodeEmitter` lives in Serde.FS.
- SourceGen no longer emits STJ code.
- STJ implements `ISerdeCodeEmitter`.
- `SerdeJson.useAsDefault()` registers the STJ emitter.
- A Debug backend exists and works.
- All tests pass after updates.
- SampleApp works with STJ backend.
- No reflection fallback is introduced.

---
