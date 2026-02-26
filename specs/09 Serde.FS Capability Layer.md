# 📘 Serde.FS — SerdeTypeInfo + Capability Layer + Attribute Interpretation

## 🎯 Goal

Introduce a **Serde‑specific semantic layer** on top of the structural metadata (`TypeInfo`, `TypeKind`, etc.) so that backends (e.g., Serde.FS.STJ) can consume a clean, fully interpreted model:

- what to serialize/deserialize  
- what to skip  
- how to name things  

This spec **does not** include any backend codegen.  
It only defines the Serde semantic layer.

---

## 1. Types to Introduce

All types live in the `Serde.FS` namespace.

### 1.1 `SerdeCapability`

Reuse/confirm this type:

```fsharp
type SerdeCapability =
    | Serialize
    | Deserialize
    | Both
```

**Rules:**

- Default capability for a type is `Both`.
- Attributes can restrict capability (see below).

---

### 1.2 `SerdeAttributes`

Introduce a type to represent Serde semantics for a type/field/case:

```fsharp
type SerdeAttributes = {
    Rename: string option
    Skip: bool
    SkipSerialize: bool
    SkipDeserialize: bool
    // Future: union tagging strategy, case renaming, etc.
}
```

**Defaults:**

```fsharp
module SerdeAttributes =
    let empty = {
        Rename = None
        Skip = false
        SkipSerialize = false
        SkipDeserialize = false
    }
```

---

### 1.3 `SerdeFieldInfo`

Serde‑aware field metadata:

```fsharp
type SerdeFieldInfo = {
    Name: string              // effective name after rename
    RawName: string           // original F# name
    Type: TypeInfo            // structural metadata
    Attributes: SerdeAttributes
    Capability: SerdeCapability
}
```

---

### 1.4 `SerdeUnionCaseInfo`

Serde‑aware union case metadata:

```fsharp
type SerdeUnionCaseInfo = {
    CaseName: string          // effective name after rename
    RawCaseName: string       // original F# case name
    Fields: SerdeFieldInfo list
    Tag: int option
    Attributes: SerdeAttributes
}
```

---

### 1.5 `SerdeTypeInfo`

Serde‑aware type metadata:

```fsharp
type SerdeTypeInfo = {
    Raw: TypeInfo             // structural metadata
    Capability: SerdeCapability
    Attributes: SerdeAttributes
    Fields: SerdeFieldInfo list option
    UnionCases: SerdeUnionCaseInfo list option
}
```

**Notes:**

- `Fields` is populated for record/anonymous record types.
- `UnionCases` is populated for union types.
- For other kinds (primitive, option, list, etc.), both are `None`.

---

## 2. Serde Attributes in F# Code

Claude should assume the following attribute types exist (or create them if needed) in `Serde.FS`:

```fsharp
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum)>]
type SerdeAttribute() =
    inherit System.Attribute()

[<AttributeUsage(AttributeTargets.All)>]
type SerdeRenameAttribute(name: string) =
    inherit System.Attribute()
    member _.Name = name

[<AttributeUsage(AttributeTargets.All)>]
type SerdeSkipAttribute() =
    inherit System.Attribute()

[<AttributeUsage(AttributeTargets.All)>]
type SerdeSkipSerializeAttribute() =
    inherit System.Attribute()

[<AttributeUsage(AttributeTargets.All)>]
type SerdeSkipDeserializeAttribute() =
    inherit System.Attribute()

[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum)>]
type SerdeSerializeAttribute() =
    inherit System.Attribute()

[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum)>]
type SerdeDeserializeAttribute() =
    inherit System.Attribute()
```

---

## 3. Attribute Interpretation Rules

Claude must implement logic (in SourceGen or a Serde.FS helper module) to:

- read attributes from types, fields, and union cases  
- interpret them into `SerdeAttributes` and `SerdeCapability`  
- compute effective names  

### 3.1 Capability resolution

Given a type’s attributes:

- If `[<SerdeSerialize>]` is present and `[<SerdeDeserialize>]` is not:
  - `Capability = Serialize`
- If `[<SerdeDeserialize>]` is present and `[<SerdeSerialize>]` is not:
  - `Capability = Deserialize`
- If both are present:
  - `Capability = Both`
- If neither is present:
  - `Capability = Both` (default)

Field/case capability:

- Inherit type capability by default.
- If `Skip = true`, treat as:
  - `Capability = Both`, but backends will ignore due to `Skip`.
- If `SkipSerialize = true`, then:
  - If type capability is `Both` → field capability is `Deserialize`
  - If type capability is `Serialize` → field effectively skipped
- If `SkipDeserialize = true`, then:
  - If type capability is `Both` → field capability is `Serialize`
  - If type capability is `Deserialize` → field effectively skipped

(Backends will later use this to decide what to emit.)

---

### 3.2 Rename resolution

For any type/field/case:

- If `[<SerdeRename("foo")>]` is present:
  - `Attributes.Rename = Some "foo"`
  - Effective name = `"foo"`
- Otherwise:
  - Effective name = original F# name

For `SerdeFieldInfo`:

- `RawName` = original F# field name  
- `Name` = effective name after rename  

For `SerdeUnionCaseInfo`:

- `RawCaseName` = original F# case name  
- `CaseName` = effective name after rename  

---

### 3.3 Skip flags

For any type/field/case:

- `[<SerdeSkip>]`:
  - `Attributes.Skip = true`
- `[<SerdeSkipSerialize>]`:
  - `Attributes.SkipSerialize = true`
- `[<SerdeSkipDeserialize>]`:
  - `Attributes.SkipDeserialize = true`

These flags do **not** remove the item from metadata.  
They are semantic hints for backends.

---

## 4. Mapping Structural Metadata → Serde Metadata

Claude must implement a function (or equivalent) that takes a `TypeInfo` and produces a `SerdeTypeInfo`.

High‑level shape:

```fsharp
module SerdeMetadataBuilder =

    val buildSerdeTypeInfo : TypeInfo -> SerdeTypeInfo
```

### 4.1 Type‑level mapping

For a given `TypeInfo`:

1. Read type‑level attributes.
2. Compute:
   - `Capability : SerdeCapability`
   - `Attributes : SerdeAttributes`
3. If `TypeInfo.Kind` is:
   - `Record` or `AnonymousRecord`:
     - Build `Fields : SerdeFieldInfo list` from structural `FieldInfo list`.
   - `Union`:
     - Build `UnionCases : SerdeUnionCaseInfo list` from structural `UnionCase list`.
   - Otherwise:
     - `Fields = None`
     - `UnionCases = None`

---

### 4.2 Field‑level mapping

For each structural `FieldInfo`:

1. Read field attributes.
2. Compute `SerdeAttributes` for the field.
3. Compute effective `SerdeCapability` for the field based on:
   - type capability
   - field skip flags
4. Compute:
   - `RawName` = original field name
   - `Name` = effective name (rename or original)
5. Keep `Type` as the structural `TypeInfo`.

---

### 4.3 Union case mapping

For each structural `UnionCase`:

1. Read case attributes.
2. Compute `SerdeAttributes` for the case.
3. Compute:
   - `RawCaseName` = original case name
   - `CaseName` = effective name (rename or original)
4. Map each case field to `SerdeFieldInfo` using the same rules as record fields.
5. Preserve `Tag` from structural metadata.

---

## 5. Out of Scope

Claude must **not** implement in this spec:

- Any changes to Serde.FS.STJ  
- Any code emission  
- Any resolver emission  
- Union tagging strategy (beyond storing `Tag` from structural metadata)  
- JSON naming conventions (e.g., camelCase)  

This spec is **semantic metadata only**.

---

## 6. Acceptance Criteria

- `SerdeTypeInfo`, `SerdeFieldInfo`, `SerdeUnionCaseInfo`, `SerdeAttributes` compile and are used.
- A function (or equivalent) exists that maps `TypeInfo` → `SerdeTypeInfo`.
- Attributes on types, fields, and union cases are correctly interpreted into:
  - `SerdeAttributes`
  - `SerdeCapability`
  - effective names
- Record and union types populate `Fields` / `UnionCases` appropriately.
- Non‑record/union types have `Fields = None` and `UnionCases = None`.
- No backend code is touched.

---
