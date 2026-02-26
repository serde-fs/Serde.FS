# 📘 **TypeKindExtractor — AttributeInfo Extraction Spec (Follow‑Up to TypeKind)**

## 🎯 **Goal**

Extend the structural metadata model (`TypeInfo`, `FieldInfo`, `UnionCase`) to include **raw attribute data**, and update the TypeKindExtractor to populate it.

This enables the Serde capability layer to interpret attributes without needing to re‑query the compiler or maintain parallel metadata.

This spec is **structural only** — no Serde semantics, no backend logic.

---

# 1. **New Structural Type: `AttributeInfo`**

Add this to `Serde.FS` (or wherever TypeKind lives):

```fsharp
type AttributeInfo = {
    Name: string
    ConstructorArgs: obj list
    NamedArgs: (string * obj) list
}
```

### Requirements

- `Name` must be the **full attribute type name**, e.g. `"Serde.FS.SerdeRenameAttribute"`.
- `ConstructorArgs` must contain the raw constructor argument values.
- `NamedArgs` must contain `(propertyName, value)` pairs for named arguments.

### Notes

- Values may be boxed (`obj`) — no need for deep type fidelity.
- No Serde interpretation here; this is raw structural metadata.

---

# 2. **Extend Structural Metadata Types**

Modify the following types to include attributes:

### 2.1 `TypeInfo`

Add:

```fsharp
Attributes: AttributeInfo list
```

### 2.2 `FieldInfo`

Add:

```fsharp
Attributes: AttributeInfo list
```

### 2.3 `UnionCase`

Add:

```fsharp
Attributes: AttributeInfo list
```

This keeps the structural metadata self‑contained and FS.Gen‑ready.

---

# 3. **Extraction Rules (TypeKindExtractor)**

Claude must update the extractor to populate `AttributeInfo` for:

- types  
- record fields  
- anonymous record fields  
- union cases  
- union case fields  

### 3.1 Attribute Discovery

Use FSharp.Compiler.Service (FCS) APIs to read attributes from:

- `FSharpEntity.Attributes`
- `FSharpField.Attributes`
- `FSharpUnionCase.Attributes`

### 3.2 AttributeInfo Construction

For each attribute:

- Extract the full attribute type name.
- Extract constructor arguments:
  - Use `Attribute.ConstructorArguments`
  - Convert each to an `obj`
- Extract named arguments:
  - Use `Attribute.NamedArguments`
  - Convert each `(name, value)` to `(string * obj)`

### 3.3 Attribute Filtering

**Do not filter.**  
All attributes must be included, not just Serde ones.

Serde.FS will interpret them later.

---

# 4. **Tests**

Add tests verifying:

### 4.1 Type‑level attributes

```fsharp
[<SerdeRename("Foo")>]
type X = { A: int }
```

- `TypeInfo.Attributes` contains one entry
- Name = `"Serde.FS.SerdeRenameAttribute"`
- ConstructorArgs = `["Foo"]`

### 4.2 Field‑level attributes

```fsharp
type X = { [<SerdeSkip>] A: int }
```

- `FieldInfo.Attributes` contains one entry

### 4.3 Union case attributes

```fsharp
type U =
    | [<SerdeRename("Bar")>] C of int
```

- `UnionCase.Attributes` contains one entry

### 4.4 Non‑Serde attributes

```fsharp
[<Obsolete("x")>]
type X = { A: int }
```

- `AttributeInfo` includes `ObsoleteAttribute`

### 4.5 Named arguments

```fsharp
[<SomeAttr(Name = "abc", Count = 3)>]
```

- `NamedArgs = [("Name", "abc"); ("Count", 3)]`

---

# 5. **Out of Scope**

Claude must **not**:

- interpret attributes  
- apply Serde semantics  
- modify SerdeTypeInfo  
- modify STJ backend  
- implement strict mode behavior  
- implement union tagging  

This is **structural metadata only**.

---

# 6. **Acceptance Criteria**

- Structural metadata types compile with new `Attributes` fields.
- TypeKindExtractor populates attributes for:
  - types  
  - fields  
  - union cases  
- Tests cover constructor args, named args, and multiple attributes.
- No Serde logic is added.
- No backend code is touched.

---
