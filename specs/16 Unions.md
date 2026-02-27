## Union representation in JSON

F# unions are sum types. STJ has no built‑in support for them, so Serde.FS must define a canonical encoding. The encoding must be:

- unambiguous  
- round‑trippable  
- stable across versions  
- compatible with strict mode  
- compatible with rename attributes  
- compatible with nullary and payload cases  

The recommended encoding is the same one used by Rust Serde:

### Tagged object encoding

```json
{ "CaseName": <payload> }
```

Examples:

- Nullary case:

  ```json
  { "None": null }
  ```

- Single-field case:

  ```json
  { "Some": 42 }
  ```

- Multi-field case (tuple-like):

  ```json
  { "Point": [10, 20] }
  ```

- Record-like case:

  ```json
  { "Person": { "Name": "A", "Age": 30 } }
  ```

This encoding is:

- simple  
- unambiguous  
- matches Serde conventions  
- works with strict mode  
- works with rename attributes  
- works with nested types  

---

## Structural detection

Unions are already represented as:

```fsharp
TypeKind.Union of UnionCase list
```

Each `UnionCase` includes:

- `CaseName`
- `Fields: FieldInfo list`
- `Tag: int option`
- `Attributes: AttributeInfo list`

The STJ backend detects unions via:

```fsharp
match serdeTypeInfo.Raw.Kind with
| Union cases -> emitUnion serdeTypeInfo cases
```

---

## Serde semantics for unions

SerdeTypeInfo must compute:

- effective case names (rename or original)
- case-level capability
- field-level capability
- skip/skipSerialize/skipDeserialize behavior
- strict mode behavior

SerdeUnionCaseInfo already models this.

---

## JSON encoding rules

### Nullary cases

```fsharp
| A
```

Serialize:

```json
{ "A": null }
```

Deserialize:

- Expect an object with exactly one property
- Property name must match the case name
- Value must be `null`

### Single-field cases

```fsharp
| B of int
```

Serialize:

```json
{ "B": 123 }
```

Deserialize:

- Expect `{ "B": <value> }`
- Deserialize `<value>` using the field’s SerdeTypeInfo

### Multi-field tuple-like cases

```fsharp
| C of int * string
```

Serialize:

```json
{ "C": [123, "x"] }
```

Deserialize:

- Expect `{ "C": [v1, v2] }`
- Deserialize each element using the corresponding SerdeTypeInfo

### Multi-field record-like cases

```fsharp
| D of { Name: string; Age: int }
```

Serialize:

```json
{ "D": { "Name": "A", "Age": 30 } }
```

Deserialize:

- Expect `{ "D": { ... } }`
- Deserialize using record logic

---

## Capability handling

Respect:

- type-level capability
- case-level capability
- field-level capability

If a case is skipped:

- Serialization: skip the case entirely
- Deserialization: reject if encountered

If a field is skipped:

- Serialization: omit the field
- Deserialization: reject if present (strict mode) or ignore (non-strict mode)

---

## Fully qualified type resolution

Case payload types must use the same FQ logic as nested records, tuples, and maps.

Example:

```fsharp
type Address = { Street: string }
type U = | Home of Address
```

Generated C# must reference:

```csharp
My.App.Address
```

---

## Strict mode behavior

Strict mode must enforce:

- unknown case → fail
- unknown field → fail
- missing required field → fail
- opaque payload type → fail
- mismatched payload shape → fail

Non-strict mode may:

- ignore unknown fields inside record-like payloads
- allow unknown cases only if skipDeserialize is set (rare)

---

## STJ integration

Add a new branch:

```fsharp
| Union cases -> emitUnion serdeTypeInfo cases
```

`emitUnion` must:

- generate a converter or JsonTypeInfo node
- serialize using tagged object encoding
- deserialize by:
  - reading the single property name
  - matching it to a case
  - delegating to the appropriate payload converter

---

## Tests

### Nullary case

```fsharp
type U = | A
```

Serialize:

```json
{ "A": null }
```

### Single-field case

```fsharp
type U = | B of int
```

Serialize:

```json
{ "B": 1 }
```

### Multi-field tuple case

```fsharp
type U = | C of int * string
```

Serialize:

```json
{ "C": [1, "x"] }
```

### Record-like case

```fsharp
type U = | D of { Name: string }
```

Serialize:

```json
{ "D": { "Name": "A" } }
```

### Renamed case

```fsharp
| [<SerdeRename("Foo")>] A
```

Serialize:

```json
{ "Foo": null }
```

### Strict mode

- unknown case → fail
- unknown field → fail
- opaque payload → fail

---

## Out of scope

- union tagging strategy variants (external, internal, adjacent)
- numeric enum mode
- polymorphic unions
- inheritance-based unions
- custom converters

---
