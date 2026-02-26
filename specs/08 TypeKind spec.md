# 📘 **Serde.FS.SourceGen — TypeKind Metadata Extraction Spec (Phase 1)**

## 🎯 **Goal**
Introduce a durable, backend‑agnostic structural metadata model (`TypeKind`, `TypeInfo`, `FieldInfo`, `UnionCase`) and update the SourceGen project to extract this metadata for any F# type. This prepares the system for the future FS.Gen extraction engine.

This spec **does not** include any backend implementation (e.g., STJ).  
It only defines the metadata model and SourceGen extraction.

---

# 🧱 **1. New Metadata Types**

All types below live in `Serde.FS` *for now*, but must be designed as if they will eventually move into FS.Gen.

### ### `PrimitiveKind`
A minimal enumeration of primitive types:

```fsharp
type PrimitiveKind =
    | Unit
    | Bool
    | Int8 | Int16 | Int32 | Int64
    | UInt8 | UInt16 | UInt32 | UInt64
    | Float32 | Float64
    | Decimal
    | String
    | Guid
    | DateTime
    | DateTimeOffset
    | TimeSpan
```

### ### `TypeKind`
The full structural type universe:

```fsharp
type TypeKind =
    | Primitive of PrimitiveKind
    | Record of fields: FieldInfo list
    | Tuple of elements: FieldInfo list
    | Option of inner: TypeInfo
    | List of inner: TypeInfo
    | Array of inner: TypeInfo
    | Set of inner: TypeInfo
    | Map of key: TypeInfo * value: TypeInfo
    | Enum of namesAndValues: (string * int) list
    | AnonymousRecord of fields: FieldInfo list
    | Union of cases: UnionCase list
```

### ### `TypeInfo`
Recursive structural metadata:

```fsharp
type TypeInfo = {
    Namespace: string option
    EnclosingModules: string list
    TypeName: string
    Kind: TypeKind
}
```

### ### `FieldInfo`
Fields reference full type metadata:

```fsharp
type FieldInfo = {
    Name: string
    Type: TypeInfo
}
```

### ### `UnionCase`
Metadata for discriminated union cases:

```fsharp
type UnionCase = {
    CaseName: string
    Fields: FieldInfo list
    Tag: int option
}
```

---

# 🧭 **2. SourceGen Extraction Rules**

Claude must update the SourceGen project to produce the new metadata.

## **2.1 General Rules**
- Extraction must be **purely structural**.  
- No Serde semantics (no capability, no naming overrides, no tagging strategy).  
- No backend logic.  
- All types must be recursively analyzed.  
- All nested types must produce their own `TypeInfo`.

---

## **2.2 Type Detection Rules**

### **Primitive**
Map F# primitive types to `PrimitiveKind`.

### **Record**
- Use FSharp.Compiler.Service to detect record types.
- Extract fields in declared order.
- Each field becomes a `FieldInfo` with recursive `TypeInfo`.

### **Tuple**
- Detect via `FSharpType.IsTuple`.
- Elements become positional fields named `"Item1"`, `"Item2"`, etc.

### **Option**
- Detect `option<'T>` via generic type definition.
- Wrap inner type in `TypeKind.Option`.

### **List / Array / Set**
- Detect via generic type definitions:
  - `list<'T>`
  - `'T []`
  - `Set<'T>`

### **Map**
- Detect `Map<'K, 'V>`.
- Extract key and value recursively.

### **Enum**
- Detect via `FSharpType.IsEnum`.
- Extract names and integer values.

### **Anonymous Record**
- Detect via `FSharpType.IsAnonymousRecord`.
- Extract fields and names.

### **Union**
- Detect via `FSharpType.IsUnion`.
- Extract cases:
  - case name  
  - fields  
  - tag (if available; otherwise `None`)  

---

# 🚫 **3. Out of Scope (Explicitly Not Included)**

Claude must **not** implement:

- Serde.FS.STJ backend logic  
- Serde capability handling  
- Serde attributes  
- Union tagging strategy  
- Naming overrides  
- Resolver generation  
- Any code emission  

This spec is **metadata only**.

---

# 🧪 **4. Acceptance Criteria**

### Metadata Model
- All types compile and form a recursive structural AST.
- No Serde-specific concepts appear in the metadata.

### SourceGen
- Given any F# type, SourceGen produces a correct `TypeInfo`.
- Nested types are fully resolved.
- All `TypeKind` cases are supported.
- No backend code is touched.

### Tests
- Add tests for:
  - primitive types  
  - simple record  
  - tuple  
  - option  
  - list/array/set/map  
  - enum  
  - anonymous record  
  - simple union  

Tests only validate metadata extraction, not serialization.

---

# 🌟 **5. Deliverables for Claude**
Claude should produce:

1. Updated `SerdeMetadata.fs` containing:
   - `PrimitiveKind`
   - `TypeKind`
   - `TypeInfo`
   - `FieldInfo`
   - `UnionCase`

2. Updated SourceGen extraction logic:
   - new recursive extractor
   - pattern matching for all `TypeKind` cases

3. Unit tests for metadata extraction.

---
