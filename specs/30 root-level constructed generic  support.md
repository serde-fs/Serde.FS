## Root‑Level Constructed Generic Support — Specification for Claude

### 1. **Explicit opt‑in requirement for generic definitions**
Serde.FS must treat generic type definitions exactly like non‑generic types:

- A generic type definition **must be explicitly marked** with `[<Serde>]` (or `[<SerdeSerialize>]` / `[<SerdeDeserialize>]`) to participate in serialization.
- If the generic definition is not marked, **no constructed generic** based on it is considered serializable.
- This rule applies uniformly whether the constructed generic appears:
  - nested inside another Serde type  
  - inside a collection  
  - inside a union case  
  - or **as the root type** passed to `Serde.Serialize` / `Serde.Deserialize`

**Example (valid):**
```fsharp
[<Serde>]
type Wrapper<'T> = Wrapper of 'T
```

**Example (invalid):**
```fsharp
type Wrapper<'T> = Wrapper of 'T   // not Serde
```

---

### 2. **Argument‑type requirement**
For a constructed generic type `Wrapper<T>` to be serializable:

- The generic definition `Wrapper<'T>` must be marked `[<Serde>]`.
- The type argument `T` must itself be Serde‑compatible:
  - either explicitly marked `[<Serde>]`
  - or a Serde‑built‑in primitive (e.g., `int`, `string`, `Guid`, etc.)

If either condition fails, the generator must emit a compile‑time error.

---

### 3. **Root‑level constructed generic discovery**
The current discovery logic correctly finds constructed generics **when nested inside other Serde types**, but **fails when the constructed generic is the root type**.

Fix:

- When the user calls `Serde.Serialize value` or `Serde.Deserialize<'T>`, the generator must treat `'T` as a **root type**.
- If `'T` is a constructed generic (e.g., `Wrapper<Person>`), the generator must:
  - add it to the discovery graph,
  - validate it using the same rules as nested types,
  - generate a serializer for the constructed generic,
  - generate serializers for any nested types inside `'T`.

This ensures:

```fsharp
Serde.Serialize (Wrapper guid)
```

works exactly the same as:

```fsharp
type Person = { Wrapped: Wrapper<Guid> }
Serde.Serialize person
```

---

### 4. **SerdeTypeInfo generation for constructed generics**
When generating serializers for a constructed generic:

- Use the sanitized identifier for module/type names (e.g., `Wrapper_System_Guid`).
- Generate a converter type specialized to the constructed generic:
  ```fsharp
  type Wrapper_System_GuidConverter() =
      inherit JsonConverter<Wrapper<Guid>>()
  ```
- Generate a `JsonTypeInfo` factory function:
  ```fsharp
  let wrapper_System_GuidJsonTypeInfo options = ...
  ```
- Ensure the converter recursively uses the correct `JsonTypeInfo` for the argument type `Guid`.

This must work for:

- single‑argument generics  
- multi‑argument generics  
- nested generics  
- generic wrappers inside collections  

---

### 5. **Error reporting**
If a constructed generic is encountered but the generic definition is not marked `[<Serde>]`, emit a compile‑time error:

**Error message requirement:**

- Must reference the generic definition, not the constructed type.
- Must clearly state that the generic definition must be marked `[<Serde>]`.

Example:

```
Error: The generic type definition 'Wrapper<'T>' must be marked with [<Serde>] to be used in serialization. 
The constructed type 'Wrapper<Guid>' cannot be serialized.
```

---

### 6. **Identifier sanitization (already implemented but required for this feature)**
All generated identifiers must be sanitized:

- Replace `.`, `+`, `` ` ``, `<`, `>`, `[`, `]`, `,` with `_`.
- Ensure module names, converter names, and JsonTypeInfo names are valid F# identifiers.

Example:

```
Wrapper<System.Guid> → Wrapper_System_Guid
```

---

## Summary of required behavior
A constructed generic type is serializable **only if**:

1. The generic definition is explicitly marked `[<Serde>]`.
2. The type argument(s) are Serde‑compatible.
3. The constructed generic is included in the discovery graph, even when used as the root type.
4. The generator emits sanitized, valid F# identifiers for all generated modules and types.

This produces a consistent, intention‑revealing, Rust‑Serde‑like model that matches user expectations and your ecosystem’s identity.

---
