### 📘 Serde.FS.STJ — Option<'T> Backend Feature Spec

#### 🎯 Goal

Teach the STJ backend to correctly serialize and deserialize `option<'T>` using the existing `SerdeTypeInfo` + `TypeInfo` model.

- Encoding: `None` → `null`, `Some x` → value of `x`
- Works for any `'T` that has Serde metadata (or is primitive/opaque)

---

### 1. Scope

**In scope:**

- Detecting `TypeKind.Option` in `SerdeTypeInfo.Raw.Kind`
- Emitting STJ converters / `JsonTypeInfo` for `option<'T>`
- Handling `None`/`Some` correctly
- Respecting `SerdeCapability` (Serialize / Deserialize / Both)
- Tests for:
  - `option<int>`
  - `option<string>`
  - `option<record>`
  - `option<option<'T>>` (nested)
  - `option<opaque>` (non‑strict mode only)

**Out of scope:**

- Tuples, lists, sets, maps, unions
- Union tagging
- Resolver emission changes beyond what’s needed to plug in Option

---

### 2. Detection

Use the structural metadata:

- If `serdeTypeInfo.Raw.Kind` is:

```fsharp
| Option innerTypeInfo -> ...
```

then this type is `option<'T>`.

- `innerTypeInfo` is the structural `TypeInfo` for `'T`.

---

### 3. STJ Encoding Rules

For any `option<'T>`:

- **Serialize:**
  - `None` → JSON `null`
  - `Some x` → JSON representation of `x` using existing Serde/STJ pipeline

- **Deserialize:**
  - JSON `null` → `None`
  - Any other JSON value → deserialize as `'T`, wrap in `Some`

This must work recursively (e.g., `option<option<int>>`).

---

### 4. Capability Handling

Respect `SerdeTypeInfo.Capability`:

- If `Capability = Serialize`:
  - Generate only serialization logic
- If `Capability = Deserialize`:
  - Generate only deserialization logic
- If `Capability = Both`:
  - Generate both

If strict mode is enabled and `'T` has no Serde metadata and is not primitive/opaque in a supported way, behavior should follow existing strict‑mode rules (e.g., fail if that’s how opaque is handled).

---

### 5. Integration Point

Wherever STJ converters / `JsonTypeInfo` are emitted today, extend the pattern match to handle `Option`:

```fsharp
match serdeTypeInfo.Raw.Kind with
| Primitive p -> ...
| Record fields -> ...
| Option inner -> emitOption inner serdeTypeInfo
| _ -> ...
```

Implement `emitOption` (or equivalent) to:

- Obtain or generate the STJ metadata for `'T` (from `inner`)
- Wrap it in an Option converter / `JsonTypeInfo` that applies the rules above

---

### 6. Tests

Add tests that:

- Round‑trip `option<int>`: `None` ↔ `null`, `Some 42` ↔ `42`
- Round‑trip `option<string>`: `None` ↔ `null`, `Some "x"` ↔ `"x"`
- Round‑trip `option<record>` using an existing record type with Serde metadata
- Round‑trip `option<option<int>>`
- Verify capability:
  - Serialize‑only type fails on deserialize
  - Deserialize‑only type fails on serialize

If strict mode is already wired, add one test asserting the expected behavior for `option<opaque>`.

---
