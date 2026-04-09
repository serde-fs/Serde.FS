# Add a Built-in Codec Factory

Use this skill when a user reports a `SerdeCodecNotFoundException` for a .NET/FSharp built-in type (e.g., `Dictionary`, `HashSet`, `Queue`, `Result`, etc.) that needs runtime codec support in Serde.FS.Json.

## Steps

### 1. Understand the type's reflection API

Write a small `dotnet fsi` script to inspect the target type:
- Properties for reading values (e.g., `Tag`, `Key`, `Value`)
- Static methods or constructors for creating instances
- Whether it implements `IEnumerable` (for collection types)
- Generic type arguments via `typedefof<T<_,_>>`

### 2. Add the codec factory module

**File:** `src/Serde.FS.Json/Codec/CollectionCodecs.fs`

Add a new module following the existing pattern:

```fsharp
module XxxCodecFactory =
    let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
        // 1. Extract inner types from typeArgs
        // 2. Resolve inner codecs: CodecResolver.resolve innerType registry
        // 3. Build the constructed generic type: typedefof<Xxx<_>>.MakeGenericType(...)
        // 4. Cache any reflection handles (properties, methods, constructors)
        // 5. Return { new IJsonCodec with Encode/Decode members }
```

Key conventions:
- The factory function signature is always `Type[] -> CodecRegistry -> IJsonCodec`
- Use the untyped `IJsonCodec` interface (not `IJsonCodec<'T>`)
- Resolve inner type codecs via `CodecResolver.resolve` for recursive support
- Cache reflection handles (PropertyInfo, MethodInfo, etc.) outside the `IJsonCodec` implementation for performance

### 3. Register the factory in BOTH registries

This is critical -- there are two places and both must be updated:

**File:** `src/Serde.FS.Json/Codec/JsonCodecRegistry.fs`
```fsharp
|> CodecRegistry.addFactory (typedefof<Xxx<_>>, CollectionCodecs.XxxCodecFactory.create)
```

**File:** `src/Serde.FS.Json/Codec/GlobalCodecRegistry.fs`
```fsharp
|> CodecRegistry.addFactory (typedefof<Xxx<_>>, CollectionCodecs.XxxCodecFactory.create)
```

`JsonCodecRegistry.create()` is the one that matters at runtime -- `SerdeJson.registerCodecs` rebuilds from it and replaces `GlobalCodecRegistry.Current`. Missing the `JsonCodecRegistry` registration will cause the factory to work in tests but fail at runtime.

### 4. Add tests

**File:** `src/Serde.FS.Json.Tests/CodecTests.fs`

Add tests covering:
- Encode produces expected JSON shape
- Decode reconstructs the value
- Round-trip (encode then decode equals original)
- Resolution via `CodecResolver.resolve` against `GlobalCodecRegistry.Current`
- Invalid JSON input throws

### 5. Add to the SampleRpc project

**File:** `src/Serde.FS.Json.SampleRpc.Shared/Domain.fs`
- Add a new method to `IOrderApi` that uses the new type in its return type or parameter

**File:** `src/Serde.FS.Json.SampleRpc.Server/OrderApi.fs`
- Implement the new method with a simple stub

**File:** `src/Serde.FS.Json.SampleRpc.Client/Program.fs`
- Add a call to exercise the new method

### 6. Build and test

```bash
dotnet test src/Serde.FS.Json.Tests/
dotnet fsi debug-build.fsx
```

The debug build pipeline packs all projects and restores the SampleRpc apps against the local feed, which validates the full end-to-end.
