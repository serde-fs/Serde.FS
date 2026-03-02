# ⭐ Claude Spec — Wire Generated Metadata into STJ + AOT‑Safe Strict Mode

This spec assumes codegen inversion is underway or complete and that the generator can emit an `IJsonTypeInfoResolver` for STJ.

Goals:

- Attach generated metadata to the cached `JsonSerializerOptions` instance used by the STJ backend.
- Rework strict mode to enforce “metadata must exist” instead of using attribute‑based reflection.
- Keep everything AOT‑safe and reflection‑free for user types.

---

## 1. Attach generated metadata to cached JsonSerializerOptions

### 1.1. Current shape (for reference)

```fsharp
namespace Serde.FS.STJ

open System.Text.Json

module internal JsonOptionsCache =
    /// Cached JsonSerializerOptions instance used by the STJ backend.
    /// Creating new options per call is expensive and breaks metadata caching.
    /// This instance is initialized once and reused for all serialization.
    let defaultJsonOptions =
        let opts = JsonSerializerOptions()
        // TODO: attach generated metadata here
        opts

type JsonBackend() =
    interface ISerdeBackend with
        member _.Serialize(value, _options) =
            JsonSerializer.Serialize(value, JsonOptionsCache.defaultJsonOptions)

        member _.Deserialize(json, _options) =
            JsonSerializer.Deserialize<'T>(json, JsonOptionsCache.defaultJsonOptions)
```

### 1.2. Requirement: register generated resolver on `defaultJsonOptions`

1. The generator must emit an STJ resolver, e.g.:

   ```fsharp
   open System.Text.Json.Serialization.Metadata

   type SerdeJsonGeneratedResolver() =
       interface IJsonTypeInfoResolver with
           member _.GetTypeInfo(t, options) =
               // generated JsonTypeInfo for supported types
               // or null if not handled
               ...
   ```

2. In `JsonOptionsCache.defaultJsonOptions`, register this resolver on the cached options instance:

   ```fsharp
   module internal JsonOptionsCache =
       let defaultJsonOptions =
           let opts = JsonSerializerOptions()
           // Attach generated metadata resolver at the front of the chain
           let resolver = SerdeJsonGeneratedResolver()
           opts.TypeInfoResolverChain.Insert(0, resolver)
           opts
   ```

3. All serialization/deserialization in `JsonBackend` must use `defaultJsonOptions` (already true) so that:

   - STJ sees the generated metadata.
   - `GetTypeInfo` returns the generated `JsonTypeInfo` for supported types.
   - No new options instances are created per call.

---

## 2. Rework strict mode to check for generated metadata (AOT‑safe)

### 2.1. Remove attribute‑based strict enforcement

Delete the existing strict enforcement logic that uses:

```fsharp
Attribute.IsDefined(ty, typeof<SerdeAttribute>)
Attribute.IsDefined(ty, typeof<SerdeSerializeAttribute>)
Attribute.IsDefined(ty, typeof<SerdeDeserializeAttribute>)
```

This reflection over user types is **not AOT‑safe** and must not be used.

### 2.2. New strict enforcement algorithm

Strict mode must enforce:

> “If strict mode is enabled, serialization/deserialization is only allowed when generated metadata exists for the type.”

Implementation outline (in STJ backend):

1. Add a helper in `JsonBackend` (or a small internal module) to enforce strictness:

   ```fsharp
   module internal JsonStrict =
       let inline enforceStrict<'T>() =
           if Serde.Strict then
               let ty = typeof<'T>
               let typeInfo = JsonOptionsCache.defaultJsonOptions.GetTypeInfo(ty)
               if obj.ReferenceEquals(typeInfo, null) then
                   failwithf
                       "Strict mode is enabled: no generated metadata found for type '%s'. \
                        Call SerdeJson.allowReflectionFallback() to allow reflection-based serialization."
                       ty.FullName
   ```

   Notes:

   - Uses `GetTypeInfo` on the **cached** options instance.
   - Does **not** use attributes.
   - Does **not** use reflection over user types.
   - Is AOT‑safe: it only queries the resolver chain.

2. Call `JsonStrict.enforceStrict<'T>()` at the start of both `Serialize` and `Deserialize`:

   ```fsharp
   type JsonBackend() =
       interface ISerdeBackend with
           member _.Serialize(value, _options) =
               JsonStrict.enforceStrict<'T>()
               JsonSerializer.Serialize(value, JsonOptionsCache.defaultJsonOptions)

           member _.Deserialize(json, _options) =
               JsonStrict.enforceStrict<'T>()
               JsonSerializer.Deserialize<'T>(json, JsonOptionsCache.defaultJsonOptions)
   ```

   (Adjust generic constraints / signatures as needed to access `'T`.)

### 2.3. Behavior

- **Strict = true (default):**
  - If generated metadata exists for `'T` → serialization/deserialization succeeds using generated converters.
  - If no metadata exists for `'T` → strict mode throws with a clear error message.
  - No reflection fallback is allowed.

- **Strict = false:**
  - `JsonStrict.enforceStrict<'T>()` is a no‑op.
  - STJ is allowed to use reflection fallback for types without generated metadata.
  - This mode is not AOT‑safe and is opt‑in via `SerdeJson.allowReflectionFallback()`.

---

## 3. Tests and acceptance criteria

### 3.1. Tests

1. **Strict ON, generated type:**

   - Given a type `'T` with generated metadata:
     - `Serde.Serialize<'T>` succeeds.
     - `Serde.Deserialize<'T>` succeeds.
     - No reflection fallback is used.

2. **Strict ON, non‑generated type:**

   - Given a type `'U` with no generated metadata:
     - `Serde.Serialize<'U>` throws with the strict‑mode error.
     - `Serde.Deserialize<'U>` throws with the strict‑mode error.

3. **Strict OFF, non‑generated type:**

   - After calling `SerdeJson.allowReflectionFallback()`:
     - `Serde.Serialize<'U>` succeeds via STJ reflection.
     - `Serde.Deserialize<'U>` succeeds via STJ reflection.

4. **AOT‑safety sanity check (conceptual):**

   - No use of `Attribute.IsDefined` or other reflection over user types in strict enforcement.
   - Strict enforcement relies solely on `GetTypeInfo` from the cached options.

### 3.2. Acceptance criteria

- Generated STJ metadata is attached to the **cached** `JsonSerializerOptions` instance via a generated `IJsonTypeInfoResolver`.
- `JsonBackend` uses the cached options instance for all serialization/deserialization.
- Strict mode:
  - **does not** use attribute‑based reflection.
  - **does** enforce “metadata must exist” via `GetTypeInfo`.
- With strict mode ON:
  - Types without generated metadata cause a clear, deterministic failure.
  - Types with generated metadata use the generated converters.
- With strict mode OFF:
  - STJ reflection fallback is allowed as an explicit, opt‑in escape hatch.
- No AOT‑unsafe reflection over user types is used in strict enforcement.

---
