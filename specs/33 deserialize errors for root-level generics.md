## Serde.FS Core Runtime Contract

### Metadata lookup
Serde.FS core exposes a single entry point for retrieving metadata:

```fsharp
module SerdeMetadata =
    val get : System.Type -> TypeMetadata
```

This function is the **only** supported way for any backend to obtain metadata. It enforces the following rules:

- If metadata exists for the requested closed type, return it.
- If metadata does not exist, throw `SerdeMissingMetadataException`.

Backends must not:
- construct metadata themselves  
- fall back to reflection  
- guess user intent  
- silently skip missing metadata  

All backends must call `SerdeMetadata.get` before performing any serialization or deserialization.

---

## SerdeMissingMetadataException

### Purpose
Thrown when a closed generic type is inferred at runtime but no metadata was generated for it at compile time. This is a violation of the Serde.FS type‑closure contract.

### Definition
```fsharp
namespace Serde

type SerdeMissingMetadataException(message: string, inferredType: System.Type) =
    inherit System.Exception(message)
    member _.InferredType = inferredType
```

### Required message format
The exception message must follow this structure:

```
Serde.FS: Missing metadata for type '<TYPE>'.

This type was inferred at runtime, but no metadata was generated for it.
Generic types require explicit type arguments when calling Deserialize<T>.

Add `<TYPE>` to the call site to generate metadata.
```

Where `<TYPE>` is the fully qualified name of the inferred closed type.

### When this exception must be thrown
- The runtime requests metadata for a closed generic type.
- The generator did not produce metadata for that type.
- The type was not explicitly written in source with concrete type arguments.

This is the only runtime error path related to metadata.

---

## Generator Metadata Emission Rules

### When metadata must be generated
The generator must emit metadata for:

- all closed generic types that appear explicitly in source  
- all non‑generic types used in serialization or deserialization  
- all nested types reachable from those types  

### When metadata must not be generated
The generator must not emit metadata for:

- open generic types  
- inferred generic types  
- types that never appear explicitly in source  

This ensures deterministic, predictable metadata generation.

---

## Compile‑Time Diagnostic Rule

### When to emit a diagnostic
Emit a compile‑time diagnostic when all of the following are true:

- The user calls `Deserialize` without specifying `<T>`.
- The target type is visible in the syntax (e.g., via a type annotation).
- The target type is a constructed generic type.
- Metadata for that type would be required.

### Example
```fsharp
let x : Wrapper<Person> = Serde.Deserialize json
```

This must produce a diagnostic instructing the user to write:

```fsharp
let x = Serde.Deserialize<Wrapper<Person>> json
```

### When not to emit a diagnostic
Do not emit a diagnostic when:

- the type is primitive or non‑generic  
- the type is inferred and not visible in syntax  
- the type is generic but the user has not annotated it  

In these cases, enforcement happens at runtime via `SerdeMissingMetadataException`.

---

## Backend Contract

Every backend must:

- call `SerdeMetadata.get typeof<'T>` before deserializing  
- rely entirely on the returned metadata  
- never attempt fallback logic  
- never attempt to construct metadata  
- never attempt to infer missing metadata  

Backends must treat missing metadata as a fatal error surfaced through the core exception.

---

## Acceptance Criteria

Claude’s implementation is correct if:

- `SerdeMetadata.get` is the only metadata lookup path.
- Missing metadata always results in `SerdeMissingMetadataException`.
- The exception message matches the required format.
- Compile‑time diagnostics fire only when the type is visible and generic.
- No backend implements its own metadata logic.
- Metadata is generated only for explicitly closed types.
- Runtime behavior is deterministic and consistent across backends.

---
