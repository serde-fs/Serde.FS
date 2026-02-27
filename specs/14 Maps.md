## Map behavior in STJ

STJ uses two different encodings depending on whether the key type is a string-like primitive.

- **String-keyed maps** (`Map<string,'V>` or any key type that STJ treats as a JSON object property name) serialize as JSON objects:
  - `Map [("a",1); ("b",2)]` → `{ "a": 1, "b": 2 }`
- **Non-string-keyed maps** (`Map<int,'V>`, `Map<Guid,'V>`, `Map<DateTime,'V>`, etc.) serialize as JSON arrays of key/value pairs:
  - `Map [(1,"x"); (2,"y")]` → `[ [1,"x"], [2,"y"] ]`

Serde.FS must mirror this behavior exactly.

---

## Structural detection

The structural metadata already represents maps as:

```fsharp
TypeKind.Map of key: TypeInfo * value: TypeInfo
```

The STJ backend should detect maps via:

```fsharp
match serdeTypeInfo.Raw.Kind with
| Map (keyInfo, valueInfo) -> emitMap serdeTypeInfo keyInfo valueInfo
```

---

## Determining the encoding mode

The backend must determine whether the key type is “string-like” in the STJ sense. This includes:

- `string`
- `System.String`
- `char` (STJ allows char keys)
- `Guid` (STJ allows Guid keys as strings)
- `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` (STJ converts these to strings)
- `Uri`
- `Enum` (STJ converts enums to strings when used as dictionary keys)

The simplest rule is:

**If STJ would serialize the key type as a JSON string, use object encoding. Otherwise, use array encoding.**

Serde.FS should implement a helper:

```fsharp
val isStringLikeKey : SerdeTypeInfo -> bool
```

This should return true for:

- PrimitiveKind.String
- PrimitiveKind.Guid
- PrimitiveKind.DateTime
- PrimitiveKind.DateTimeOffset
- PrimitiveKind.DateOnly
- PrimitiveKind.TimeOnly
- PrimitiveKind.Char
- PrimitiveKind.Enum (once enums are implemented)

Everything else → false.

---

## JSON encoding rules

### Object encoding (string-like keys)

Serialize:

```json
{
  "key1": <value1>,
  "key2": <value2>
}
```

Deserialize:

- Iterate object properties
- Convert property name to key type using the key’s SerdeTypeInfo
- Convert property value to value type

### Array encoding (non-string-like keys)

Serialize:

```json
[
  [ <key1>, <value1> ],
  [ <key2>, <value2> ]
]
```

Deserialize:

- Expect a JSON array
- For each element, expect a 2-element array
- Deserialize element[0] as key
- Deserialize element[1] as value

---

## Capability handling

Respect `SerdeTypeInfo.Capability`:

- Serialize-only → only emit serialization logic
- Deserialize-only → only emit deserialization logic
- Both → emit both

Field-level skip attributes should not apply to map entries (only to the map field itself).

---

## Fully qualified type resolution

Both key and value types must use the same FQ type resolution logic used for nested records and tuples.

Example:

```fsharp
type Address = { Street: string }
type User = { Addresses: Map<string, Address> }
```

Generated C# must reference:

```csharp
Dictionary<string, My.App.Address>
```

or the equivalent converter-based representation.

---

## Strict mode behavior

Strict mode applies recursively:

- If key or value type is opaque → strict mode must reject the map
- If both key and value types have Serde metadata or are primitives → allowed

Strict mode does not change the encoding mode.

---

## Tests

### String-keyed map

```fsharp
type T = { M: Map<string,int> }
```

Serialize:

```json
{ "M": { "a": 1, "b": 2 } }
```

### Non-string-keyed map

```fsharp
type T = { M: Map<int,string> }
```

Serialize:

```json
{ "M": [ [1,"x"], [2,"y"] ] }
```

### Nested map

```fsharp
type T = { M: Map<string, Map<int,string>> }
```

### Map of records

```fsharp
type Address = { Street: string }
type T = { M: Map<string, Address> }
```

### Map inside option/list/array/set

```fsharp
type T = { Items: Map<int,string> list }
```

### Strict mode

- Map<string,Opaque> → allowed in non-strict mode, rejected in strict mode
- Map<Opaque,string> → same behavior

---

## Out of scope

This spec does not include:

- Enum implementation (but map must be ready for it)
- Union implementation
- Naming conventions
- Resolver emission changes beyond map support

---
