### Spec H — Pure Serde JSON Backend

---

## 1. Goals

- Remove `System.Text.Json` reader/writer from the **SerdeJson** path.
- Implement a **Serde‑native JSON writer and reader** used by:
  - `SerdeJson.serialize`
  - `SerdeJson.serializeToUtf8`
  - `SerdeJson.deserialize`
  - `SerdeJson.deserializeFromUtf8`
- Keep codecs, registry, attributes, and override rules **unchanged**.
- Keep STJ only for:
  - `JsonTypeInfoBuilder`
  - legacy/interop scenarios.

---

## 2. Scope

### In scope

- New **JSON writer** (UTF‑8) for `JsonValue -> byte[]/string`.
- New **JSON reader** (UTF‑8) for `byte[]/string -> JsonValue`.
- Wire SerdeJson APIs to use these new components.
- Remove Utf8JsonWriter/Utf8JsonReader from SerdeJson implementation.

### Out of scope

- No changes to:
  - `IJsonCodec<'T>` / encoder/decoder interfaces
  - codec registry
  - attributes (`Serde`, `SerdeField`, etc.)
  - override rules
  - JsonTypeInfoBuilder behavior

---

## 3. Serde‑native JSON writer

### 3.1 API

Introduce an internal writer module, e.g.:

```fsharp
module internal SerdeJsonWriter =
    val writeToUtf8 : JsonValue -> byte[]
    val writeToString : JsonValue -> string
```

Implementation details:

- Operates on `JsonValue` (existing type).
- Produces UTF‑8 bytes or string.
- No dependency on `Utf8JsonWriter` or any STJ type.

### 3.2 Behavior

- `Null` → `null`
- `Bool b` → `true` / `false`
- `Number n` → JSON number (respect existing numeric policy; no quotes)
- `String s`:
  - Proper JSON string escaping:
    - quotes, backslashes, control chars
    - `\n`, `\r`, `\t`, `\b`, `\f`
    - `\uXXXX` for non‑printable if needed
- `Array xs`:
  - `[` elements `]`, comma‑separated
- `Object props`:
  - `{` `"name": value` pairs `}`, comma‑separated
  - property names always JSON strings

Whitespace:

- Minimal JSON (no pretty‑printing required).
- Optional: keep design open for future pretty‑printer.

---

## 4. Serde‑native JSON reader

### 4.1 API

Introduce an internal reader module, e.g.:

```fsharp
module internal SerdeJsonReader =
    val readFromUtf8 : byte[] -> JsonValue
    val readFromString : string -> JsonValue
```

Implementation details:

- Parses UTF‑8 (or string) into `JsonValue`.
- No dependency on `Utf8JsonReader`, `JsonDocument`, or any STJ type.

### 4.2 Behavior

Implement a minimal JSON tokenizer + parser:

- Skip whitespace.
- Parse:
  - `null` → `JsonValue.Null`
  - `true` / `false` → `JsonValue.Bool`
  - numbers → `JsonValue.Number` (respect existing numeric representation)
  - strings → `JsonValue.String` with unescaped content
  - arrays → `JsonValue.Array`
  - objects → `JsonValue.Object`

Error handling:

- On invalid JSON, throw a Serde‑specific exception (e.g., `SerdeJsonParseException`) with:
  - position/index
  - short message (unexpected token, unterminated string, etc.)

---

## 5. Wiring into SerdeJson

Update `SerdeJson` module (from Spec F):

### 5.1 Serialization

Current (Spec F):

- `serialize`:
  - value → codec → `JsonValue` → **Utf8JsonWriter** → string
- `serializeToUtf8`:
  - value → codec → `JsonValue` → **Utf8JsonWriter** → byte[]

Change to:

```fsharp
val serialize<'T> : 'T -> string =
    // 1. resolve codec for 'T
    // 2. codec.Encode value -> JsonValue
    // 3. SerdeJsonWriter.writeToString jsonValue

val serializeToUtf8<'T> : 'T -> byte[] =
    // 1. resolve codec for 'T
    // 2. codec.Encode value -> JsonValue
    // 3. SerdeJsonWriter.writeToUtf8 jsonValue
```

No STJ usage.

### 5.2 Deserialization

Current (Spec F):

- `deserialize`:
  - string → **Utf8JsonReader/JsonDocument** → `JsonValue` → codec → value
- `deserializeFromUtf8`:
  - byte[] → **Utf8JsonReader/JsonDocument** → `JsonValue` → codec → value

Change to:

```fsharp
val deserialize<'T> : string -> 'T =
    // 1. SerdeJsonReader.readFromString json -> JsonValue
    // 2. resolve codec for 'T
    // 3. codec.Decode jsonValue

val deserializeFromUtf8<'T> : byte[] -> 'T =
    // 1. SerdeJsonReader.readFromUtf8 bytes -> JsonValue
    // 2. resolve codec for 'T
    // 3. codec.Decode jsonValue
```

No STJ usage.

---

## 6. STJ usage after Spec H

- `SerdeJson` path:
  - **must not** reference any STJ types.
- `JsonTypeInfoBuilder`:
  - remains for STJ integration only.
- Any STJ‑based APIs should be clearly documented as:
  - interoperability/legacy
  - not the primary Serde path.

---

## 7. Error model

- JSON parse errors:
  - `SerdeJsonParseException` (or similar), with:
    - position
    - offending char/token
    - short description.
- Codec resolution errors:
  - unchanged from Spec F.
- Codec decode errors:
  - unchanged from Spec F (propagate or wrap as currently designed).

---

## 8. Documentation updates

- Update docs to state:
  - `SerdeJson.serialize` / `deserialize` are now **pure Serde**, no STJ.
  - Behavior is deterministic and identical across platforms.
- Mention:
  - STJ is only used for `JsonTypeInfo` integration.
- Optionally add a short “Architecture” section:
  - codecs → `JsonValue` → SerdeJsonWriter/Reader → bytes/string.

---
