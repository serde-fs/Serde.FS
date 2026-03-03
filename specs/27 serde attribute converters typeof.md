## Spec: Update Serde.FS to use Djinn’s new `typeof<>` metadata and remove string‑based `Custom`

### Overview
Serde.FS currently supports per‑type custom converters via:

```fsharp
[<Serde(Custom = "MyConverter")>]
```

This string‑based mechanism was a temporary workaround because Djinn did not previously capture `typeof<T>` expressions. Djinn v0.1.4 now emits:

```fsharp
AttrArgValue.TypeOf "MyNamespace.MyConverter"
```

boxed inside `AttributeInfo.ConstructorArgs` or `NamedArgs`.

Serde.FS must be updated to:

- **Remove** the string‑based `Custom` property from the Serde attribute.
- **Require** users to specify converters using `typeof<MyConverter>`.
- **Interpret** Djinn’s new `TypeOf` metadata and resolve the converter type.
- **Validate** that the converter implements the correct interface.
- **Generate** the appropriate adapter code.

No changes are required in Djinn or TypeModel.

---

## Goals

### 1. Replace string‑based converter attribute
Remove:

```fsharp
member val Custom : string = null with get, set
```

Add:

```fsharp
member val Converter : obj = null with get, set
```

This matches how Djinn boxes attribute arguments.

Usage becomes:

```fsharp
[<Serde(Converter = typeof<MyConverter>)>]
```

### 2. Update Serde attribute parsing in SourceGen
In the Serde attribute extraction logic:

- Look for a named argument `"Converter"` in `AttributeInfo.NamedArgs`.
- Unbox the value.
- Pattern‑match on:

```fsharp
| AttrArgValue.TypeOf fqName -> ...
```

### 3. Resolve the converter type
Serde.FS already has type‑resolution helpers. Use them to:

- Resolve the fully qualified name to a `TypeInfo`.
- Validate that the type exists.
- Validate that it implements:

```fsharp
ISerdeConverter<'T>
```

for the target type.

### 4. Update SerdeTypeInfo
Add a new field:

```fsharp
ConverterType : TypeInfo option
```

Populate it when the attribute contains a `TypeOf` argument.

### 5. Update code generation
In the JSON backend:

- If `ConverterType` is present:
  - Generate a thin adapter that instantiates the converter.
  - Route serialization/deserialization through it.
- If absent:
  - Use the default behavior.

### 6. Remove all string‑based converter logic
Delete:

- The `Custom : string` property.
- Any code that interprets string‑based converter names.
- Any fallback logic that tries to resolve converter types from strings.

### 7. Update SampleApp
Add an example:

```fsharp
type UpperCaseConverter() =
    interface ISerdeConverter<string> with
        member _.Serialize(v) = v.ToUpperInvariant()
        member _.Deserialize(v) = v.ToLowerInvariant()

[<Serde(Converter = typeof<UpperCaseConverter>)>]
type FancyName = { Value : string }
```

### 8. Update tests
Add tests verifying:

- Attribute with `typeof<MyConverter>` is recognized.
- Converter type is resolved correctly.
- Codegen uses the converter.
- Removing the string‑based API does not break existing tests.

---

## Files to modify

| File | Change |
|------|--------|
| `SerdeAttribute.fs` | Remove `Custom : string`; add `Converter : obj` |
| `SerdeAttributeParser.fs` | Interpret `AttrArgValue.TypeOf` |
| `SerdeTypeInfo.fs` | Add `ConverterType : TypeInfo option` |
| `SerdeTypeInfoBuilder.fs` | Populate `ConverterType` |
| `Json/StjCodeEmitter.fs` | Generate converter‑aware code |
| `SampleApp` | Add example using `typeof<>` |
| `Tests` | Add converter tests; remove string‑based tests |

---

## Non‑Goals

- No changes to Djinn.  
- No changes to TypeModel.  
- No changes to the Djinn attribute parser.  
- No runtime reflection‑based fallback.  
- No support for both string and typeof — typeof is the only supported form.

---

## Acceptance Criteria

- Serde.FS builds against Djinn v0.1.4.
- `typeof<MyConverter>` is recognized and resolved.
- Codegen uses the converter.
- String‑based converter support is fully removed.
- SampleApp compiles and demonstrates the new API.
- All tests pass.

---
