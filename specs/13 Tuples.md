## 📘 Serde.FS.STJ — Tuple Backend Feature Spec

### 🎯 Goal

Enable correct serialization and deserialization of F# tuples in the STJ backend using the existing structural metadata (`TypeKind.Tuple`) and semantic metadata (`SerdeTypeInfo`). Tuples should serialize as JSON arrays and deserialize from JSON arrays, matching STJ’s native behavior.

This feature must work for:

- simple tuples (`int * string`)
- nested tuples (`(int * string) * bool`)
- tuples inside records, options, lists, arrays, sets, and maps
- tuples containing any supported type (primitive, record, option, list, array, set, map, tuple, enum, union, opaque)

---

## 1. Detection

Tuples are already represented structurally as:

```fsharp
TypeKind.Tuple of FieldInfo list
```

The STJ backend should detect tuples via:

```fsharp
match serdeTypeInfo.Raw.Kind with
| Tuple elements -> emitTuple serdeTypeInfo elements
```

Each element is a `FieldInfo` with:

- `Name` (Item1, Item2, …)
- `Type` (recursive `TypeInfo`)
- `Attributes` (ignored for tuples)
- `SerdeFieldInfo` will wrap these later

---

## 2. JSON Encoding Rules

Tuples must serialize as JSON arrays:

- `(1, "x")` → `[1, "x"]`
- `(1, (2, 3))` → `[1, [2, 3]]`
- `(Some 5, None)` → `[5, null]`
- `(Address, User)` → `[ {…}, {…} ]`

This matches STJ’s built‑in tuple converter behavior.

### Serialization

For a tuple `(v1, v2, ..., vn)`:

- Write `[`  
- Serialize each element using the existing Serde/STJ pipeline  
- Write `]`

### Deserialization

Given a JSON array:

- Read array start  
- For each element position `i`:
  - Deserialize using the element’s `SerdeTypeInfo`  
- Construct the tuple using the appropriate F# tuple constructor

---

## 3. Capability Handling

Respect `SerdeTypeInfo.Capability`:

- If `Serialize` only → generate serialization logic
- If `Deserialize` only → generate deserialization logic
- If `Both` → generate both

Tuple elements inherit capability from the tuple type unless overridden by field‑level skip attributes (rare for tuples, but consistent).

---

## 4. Integration with Existing Codegen

Add a new branch in the STJ emitter:

```fsharp
match serdeTypeInfo.Raw.Kind with
| Tuple elements -> emitTuple serdeTypeInfo elements
```

Implement `emitTuple` to:

- Resolve element types using the same FQ type logic used for nested records
- Generate a converter or `JsonTypeInfo` node that:
  - Serializes as JSON array
  - Deserializes from JSON array
  - Recursively delegates to element converters

This must use the same code paths as:

- Option
- List
- Array
- Set

so that nested tuples work automatically.

---

## 5. Fully Qualified Type Resolution

Tuple element types must use the same FQ type resolution logic as nested records.

Example:

```fsharp
type Address = { Street: string }
type User = string * Address
```

Generated C# must reference:

```csharp
My.App.Address
```

not just `Address`.

---

## 6. Strict Mode Behavior

Strict mode applies to tuple elements:

- If any element type is opaque and strict mode is enabled → fail
- If all element types have Serde metadata or are primitives → allowed

Tuples themselves do not introduce new strict‑mode rules.

---

## 7. Tests

Add tests for:

### 7.1 Simple tuple

```fsharp
type T = int * string
```

- Serialize `(1, "x")` → `[1, "x"]`
- Deserialize `[1, "x"]` → `(1, "x")`

### 7.2 Nested tuple

```fsharp
type T = (int * int) * string
```

- Serialize `((1,2), "x")` → `[[1,2], "x"]`

### 7.3 Tuple inside record

```fsharp
type R = { Pair: int * string }
```

- Generated C# uses correct FQ type for tuple elements

### 7.4 Tuple inside option/list/array/set

```fsharp
type R = { Items: (int * string) list }
```

- Serialize `[ [1,"x"], [2,"y"] ]`

### 7.5 Tuple containing record

```fsharp
type Address = { Street: string }
type T = string * Address
```

- Generated C# uses FQ type for `Address`

### 7.6 Strict mode

- Tuple containing opaque type fails in strict mode
- Tuple containing only supported types succeeds

---

## 8. Out of Scope

This spec does **not** include:

- Map support  
- Enum support  
- Union support  
- Naming conventions  
- Union tagging  
- Resolver emission changes beyond tuple support  

---
