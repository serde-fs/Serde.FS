# **Specification: Attribute‑Level Custom Converters for Serde.FS**

## **Goal**
Enable users to attach custom serialization/deserialization logic to a type using the `Serde` attribute. The generator must detect the converter at design time, validate it, and emit a generated serializer/deserializer that delegates to the converter. Strictness must remain fully enforced.

This feature must not introduce fallback, runtime discovery, or backend‑level mutability.

---

## **Attribute Shape**
Extend the existing `Serde` attribute with a new optional property:

```fsharp
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum)>]
type SerdeAttribute() =
    inherit Attribute()

    member val Custom : Type = null with get, set
```

Usage:

```fsharp
[<Serde(Custom = typeof<MyConverter>)>]
type Name = { Value : string }
```

---

## **Converter Interface**
Add a new interface in `Serde.FS`:

```fsharp
type ISerdeConverter<'T> =
    abstract Serialize : 'T -> JsonNode
    abstract Deserialize : JsonNode -> 'T
```

Constraints:

- Converter must have a **public parameterless constructor**.
- Converter must implement `ISerdeConverter<'T>` for the annotated type `'T`.
- Converter may internally cache expensive state (e.g., STJ options).

---

## **Generator Behavior**

### **1. Detect converter**
When processing a type with `[<Serde(Custom = typeof<X>)>]`:

- Validate that `X` exists.
- Validate that `X` implements `ISerdeConverter<'T>` where `'T` is the annotated type.
- Validate that `X` has a public parameterless constructor.
- If any validation fails → emit a **build‑time Serde error**.

### **2. Generate metadata normally**
The generator must still:

- Create a `SerdeTypeInfo` entry.
- Generate a file for the type.
- Participate in recursive strict validation.

### **3. Replace serializer/deserializer bodies**
Instead of generating field‑by‑field logic, emit:

```fsharp
override _.Serialize(value: T) =
    let c = new MyConverter()
    c.Serialize(value)

override _.Deserialize(node: JsonNode) =
    let c = new MyConverter()
    c.Deserialize(node)
```

### **4. Strictness remains unchanged**
If a nested type lacks Serde metadata:

- The generator must still fail at build time.
- Converters do **not** bypass strictness.

### **5. No backend‑level registry**
Do not add any runtime configuration or override registry.

---

## **Runtime Backend Behavior**
No changes required.

The backend continues to:

- Use generated serializers/deserializers.
- Enforce strictness when metadata is missing.
- Remain deterministic and reflection‑free.

---

## **Tests to Add**
Add tests verifying:

- A type with a valid converter compiles and serializes correctly.
- A converter missing `ISerdeConverter<'T>` causes a build‑time error.
- A converter missing a public parameterless constructor causes a build‑time error.
- A nested type missing Serde metadata still triggers strict validation.
- Converters work for both records and DUs.

---

# **SampleApp Update (Claude must add this)**

Add a new type demonstrating a custom converter:

### **1. Add a type**
```fsharp
[<Serde(Custom = typeof<UppercaseNameConverter>)>]
type FancyName = { Value : string }
```

### **2. Add the converter**
```fsharp
type UppercaseNameConverter() =
    interface ISerdeConverter<FancyName> with
        member _.Serialize(n: FancyName) =
            JsonValue.String(n.Value.ToUpperInvariant())

        member _.Deserialize(node: JsonNode) =
            let s = node.AsString()
            { Value = s.ToLowerInvariant() }
```

### **3. Use it in SampleApp**
Add a field to `Person`:

```fsharp
Fancy : FancyName
```

Initialize it:

```fsharp
Fancy = { Value = "Jordan" }
```

Print it during serialization/deserialization to demonstrate the transformation.

---

# **Acceptance Criteria**
- Attribute‑level converters work end‑to‑end.
- Generator validates converter types at build time.
- Generated code delegates to the converter.
- Strictness is preserved.
- SampleApp includes a working example.
- No fallback or runtime registry is introduced.
- No changes to backend behavior.

---
