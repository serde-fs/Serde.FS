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

### 6. Add Fable-side support

The runtime factory (steps 2-4) handles the .NET client + server. To make the new type also work in a generated **Fable** client (`[<GenerateFableClient>]`), three more pieces must be wired:

**6a. Map the SynType to a structural TypeInfo.**

**File:** `src/Serde.FS.SourceGen/RpcApiDiscovery.fs`, function `synTypeToTypeInfo`

If the new type can serialise to an existing `TypeKind` (e.g., `Dictionary` → `TypeKind.Map`, `HashSet` → `TypeKind.Set`), add a match arm in the `SynType.App(...)` case that funnels it into that kind. Example:

```fsharp
| "Dictionary", [ Some k; Some v ] ->
    Some (mkSyntheticTypeInfo "Map" (Map (k, v)))
```

If the type has no existing structural equivalent, a new `TypeKind` case is required in `FSharp.SourceDjinn.TypeModel.Types` — that's a SourceDjinn change, scope it separately.

**6b. Route the TypeKind to a FableTypeExpr (only if a new variant is needed).**

**File:** `src/Serde.FS.Json.SourceGen/FableClientEmitter.fs`, function `fromTypeInfo`

If step 6a reused an existing `TypeKind`, `fromTypeInfo` already handles it — no change. Otherwise add a new branch and a corresponding case in the `FableTypeExpr` discriminated union at the top of the file.

**6c. Implement encode/decode expressions.**

**File:** `src/Serde.FS.Json.SourceGen/FableClientEmitter.fs`, functions `encodeExpr` and `decodeExpr`

Each `FableTypeExpr` variant must produce JS-side encode and decode F# expressions. **Match the wire format of the runtime factory from step 2 exactly** — the server emits one shape and the Fable client must produce/consume the same shape, otherwise round-trips fail silently.

Example skeleton:

```fsharp
| FMap (k, v) ->
    sprintf "(%s |> ...JS-encode each pair as [k, v]... |> Array.ofSeq |> box)" varExpr
```

If a variant truly cannot be supported (e.g., open generic), return `(failwith "...")` so the user gets a clear compile-time error rather than broken JS.

**6d. Add a snapshot test.**

**File:** `src/Serde.FS.SourceGen.Tests/Fable/FableClientEmitterTests.fs`

Add a `[<Test>]` that builds a synthetic `RpcInterfaceInfo` using the new type and asserts the emitter output against a new snapshot:

```fsharp
[<Test>]
let ``record with Dictionary<string, int> field`` () =
    let ti = record "Domain" "Cache" [ "Hits", mapTi stringTi int32Ti ]
    let iface = interfaceOf "Domain" "ICacheApi" [ nullaryMethod "Get" ti ] true
    let actual = FableClientEmitter.emit iface [ toSerde ti ]
    SnapshotHarness.assertSnapshot "record_dictionary_field" actual
```

Run `dotnet test --filter "FableClientEmitterTests"` — the first run fails with a `.actual.fs` written to `Fable/Snapshots/`. Inspect it carefully; if the output is right, rename it to `.expected.fs` and commit both the test and the snapshot. May need to add a new builder in `Fable/SyntheticTypes.fs` (e.g. `mapTi`) if the new type isn't covered there.

### 7. Build and test

```bash
dotnet test src/Serde.FS.SourceGen.Tests/     # Fable emitter tests
dotnet test src/Serde.FS.Json.Tests/          # runtime factory tests
dotnet fsi debug-build.fsx                    # full end-to-end including SampleRpc
```

If the new type is exercised in `SampleRpc.Shared/Domain.fs`, the debug-build run will regenerate `SampleRpc.Shared/fable-generated/~IOrderApi.fable.g.fs` against the new emitter logic. Inspect that file to confirm the codec module shape and `FableClient` calls look right.
