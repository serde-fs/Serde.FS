# 🌟 **Serde.FS JSON Codegen Specification (Final, Authoritative)**

This document defines exactly what the JSON code generator must emit for Serde.FS.  
Claude should follow this spec *verbatim*.

---

# 1. **Generated File Naming**

For each JSON codegen run, the generator must emit **one file** with the following name:

# **`~SerdeJsonCodecs.json.g.fs`**

This matches the existing Serde/Djinn/STJ conventions:

- `~SerdeResolver.serde.g.fs`
- `~SerdeResolverRegistration.djinn.g.fs`
- `~EntryPoint.djinn.g.fs`

The pattern is:

```
~<ModuleName>.<backend>.g.fs
```

Where:

- `<ModuleName>` = `SerdeJsonCodecs`
- `<backend>` = `json`
- `.g.fs` = generated F# source

---

# 2. **Generated File Contents**

The file must contain:

1. **All generated JSON codec values**
2. **One registration function**  
   `CodecRegistry -> CodecRegistry`
3. **One registration side‑effect**  
   `do SerdeJson.registerCodecs register`
4. **No other side effects**

There must be **exactly one** registration function and **exactly one** registration call.

---

# 3. **Namespace and Module Structure**

The generated file must use:

```fsharp
namespace <UserRootNamespace>.Generated

open System
open Serde.FS.Json
open Serde.FS.Json.Codec

[<AutoOpen>]
module SerdeJsonCodecs =
    ...
```

Where `<UserRootNamespace>` is the namespace the user passed to the generator.

---

# 4. **Codec Emission Rules**

For each F# type `T`, the generator must emit:

- A value of type `IJsonCodec`
- Named `<typeName>Codec` (camelCase)

Example:

```fsharp
let private personCodec : IJsonCodec =
    { new IJsonCodec with
        member _.Encode(value: obj) = ...
        member _.Decode(json: JsonValue) = ...
    }
```

If the type is generic, the generator may emit:

- A `CodecFactory`
- And register it via `CodecRegistry.addFactory`

But this is optional and orthogonal.

---

# 5. **Registration Function**

The generator must emit **one** function:

```fsharp
let private register (reg: CodecRegistry) : CodecRegistry =
    reg
    |> CodecRegistry.add (typeof<Person>, personCodec)
    |> CodecRegistry.add (typeof<Wrapper<Person>>, wrapperPersonCodec)
    |> CodecRegistry.add (typeof<Order>, orderCodec)
    // ... one add per generated codec ...
```

Rules:

- Must be pure  
- Must not mutate global state  
- Must not create a registry  
- Must not call `SerdeJson.useAsDefault`  
- Must not call `GlobalCodecRegistry.Current <- ...`  

---

# 6. **Registration Side‑Effect**

At the bottom of the file:

```fsharp
do SerdeJson.registerCodecs register
```

This enables:

```fsharp
SerdeJson.serialize value
SerdeJson.deserialize json
```

### Optional (only if user explicitly requests):

```fsharp
// do SerdeJson.useAsDefault register
```

The generator must **not** enable this by default.

---

# 7. **What the Generator Must NOT Do**

Claude must **not**:

- Emit multiple registration functions  
- Emit multiple registration files  
- Emit registration code in per‑type codec files  
- Mutate global state directly  
- Call `SerdeJson.useAsDefault` unless explicitly instructed  
- Create its own `CodecRegistry`  
- Depend on STJ or Djinn  
- Emit runtime backend code  

JSON codegen is **codec‑only**.

---

# 8. **Full Example Output (Template)**

This is the exact shape Claude should emit:

```fsharp
namespace MyApp.Generated

open System
open Serde.FS.Json
open Serde.FS.Json.Codec

[<AutoOpen>]
module SerdeJsonCodecs =

    // -----------------------------------------------------------------------
    // Generated codecs
    // -----------------------------------------------------------------------

    let private personCodec : IJsonCodec =
        { new IJsonCodec with
            member _.Encode(value: obj) = ...
            member _.Decode(json: JsonValue) = ...
        }

    let private wrapperPersonCodec : IJsonCodec =
        { new IJsonCodec with
            member _.Encode(value: obj) = ...
            member _.Decode(json: JsonValue) = ...
        }

    // -----------------------------------------------------------------------
    // Registry registration
    // -----------------------------------------------------------------------

    let private register (reg: CodecRegistry) : CodecRegistry =
        reg
        |> CodecRegistry.add (typeof<Person>, personCodec)
        |> CodecRegistry.add (typeof<Wrapper<Person>>, wrapperPersonCodec)
        // ... more ...

    // -----------------------------------------------------------------------
    // Registration side-effect
    // -----------------------------------------------------------------------

    do SerdeJson.registerCodecs register

    // Optional runtime backend mode:
    // do SerdeJson.useAsDefault register
```

---

# 9. **Summary for Claude**

> Emit **one file** named `~SerdeJsonCodecs.json.g.fs`.  
> It must contain:
>
> - all generated JSON codecs  
> - one `register : CodecRegistry -> CodecRegistry` function  
> - one `do SerdeJson.registerCodecs register` call  
>
> No other file may contain registration logic.  
> Do not mutate global state.  
> Do not call `useAsDefault` unless explicitly instructed.

---
