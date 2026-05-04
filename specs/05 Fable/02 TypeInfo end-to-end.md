# ✅ **Spec: Carry `TypeInfo` end-to-end through the Fable client emitter**

### **Goal**

Eliminate the parallel string-parsing codepath in `FableClientEmitter` and route every type reference through `TypeInfo` from discovery to emission. The codec-naming function becomes the single source of truth — used both when generating encode/decode call sites and when emitting codec module headers — making naming drift between the two structurally impossible.

### **Background — why this matters**

The Fable client generator produces two things from a `[<RpcApi>]` interface:

1. **Codec modules** — `module private UserCodec = { encode = ...; decode = ... }`, named via `codecModuleNameFromTi (ti: TypeInfo)`.
2. **Client method bodies** — `return UserCodec.decode json`, where the codec name is computed by parsing the F# type expression *string* (`"BimHub.Domain.Auth.User"`) supplied by `RpcMethodInfo.OutputType`.

Today these two name computations live in different functions (`codecModuleNameFromTi` for the emitter, `parseTypeString → resolveAtom` for the call-site). When they disagree — for example when the parser falls back on an unknown type and produces `CEI_BimHub_Domain_Auth_UserCodec` while the emitter produced `UserCodec` — the generated file has dangling references, with errors visible only when the consuming project is opened in an IDE.

The string-parsing codepath only exists because `RpcMethodInfo.InputType` / `OutputType` are strings. Fix that and the whole drift class disappears.

---

### **Requirements**

### **1. Add `TypeInfo` to `RpcMethodInfo`**

```fsharp
type RpcMethodInfo = {
    MethodName: string
    InputType: string                       // unchanged — still used by RpcDispatchEmitter
    InputIsTupled: bool
    InputParams: string list
    OutputType: string                      // unchanged
    InputTypeInfo: TypeInfo option          // NEW
    OutputTypeInfo: TypeInfo option         // NEW
    InputParamTypeInfos: TypeInfo list      // NEW (empty when InputIsTupled = false)
}
```

The string fields stay: `RpcDispatchEmitter` splices them into generated F# code (`SerdeJson.deserialize<%s>`) and reads correctly as F# type expressions. The `TypeInfo option` fields are populated by discovery and consumed by the Fable emitter.

`Option` is used (not a non-optional `TypeInfo`) so discovery can return `None` for a built-in or unknown type without a synthesized fallback fighting the existing `InputType` string. The Fable emitter handles `None` as a discoverability error and reports it via the host.

### **2. Add `synTypeToTypeInfo` to `RpcApiDiscovery`**

A sibling of the existing `synTypeToString`. Walks `SynType`, recurses through `Async<>` / `Task<>` / generics / options / lists / tuples, and produces a `TypeInfo` whose `Kind` matches the F# type:

| F# type | Resulting `TypeInfo.Kind` |
|---|---|
| `int`, `string`, `bool`, … | `Primitive` (with appropriate `PrimitiveKind`) |
| `unit` | `Primitive PrimitiveKind.Unit` |
| `T option` / `option<T>` | `Option (synTypeToTypeInfo inner)` |
| `T list` / `list<T>` | `List …` |
| `T array` / `T[]` | `Array …` |
| `T seq` | (treat as `List` for serialization purposes) |
| `Set<T>` | `Set …` |
| `Map<K,V>` | `Map (k, v)` |
| `Result<T,E>` | `ConstructedGenericType` with `TypeName="Result"` |
| `A * B` | `Tuple [...]` |
| `Foo<T>` (user-defined generic) | `ConstructedGenericType` with looked-up base + arg `TypeInfo`s |
| Bare user type | Looked up in the discovery's existing `Map<string, TypeInfo>` lookup |

When a user type is not in the lookup the function returns `None` (lifted up through the recursion), surfaced later as a discovery diagnostic.

### **3. Replace `parseTypeString` in `FableClientEmitter`**

Delete:
- `splitTopLevelCommas`
- `splitTopLevelStars`
- `resolveAtom`
- `parseTypeString`
- `buildTypeLookup`
- The `lookup: Map<string, SerdeTypeInfo>` parameter threaded through `emit`

Replace each `parseTypeString lookup m.OutputType` call with `fromTypeInfo m.OutputTypeInfo.Value` (with `None` handled by an emitter-side error).

`fromTypeInfo` and `codecModuleNameFromTi` already do the right thing for `TypeInfo`. They become the only path; nothing computes a codec name from a string anymore.

### **4. Surface "type not in discovery lookup" as an MSBuild error**

When a method's `InputTypeInfo` / `OutputTypeInfo` is `None`, or when a `TypeInfo` recursion hits an unresolved user type, the Fable emitter must NOT emit a placeholder file. It must surface a clickable MSBuild-format error:

```
Domain/Api.fs(N,M): error SerdeFS102: [<GenerateFableClient>] cannot resolve type 'BimHub.Domain.Auth.User' referenced by 'IServerApi.GetCurrentUser'. Ensure the type is declared in a project source file walked by Serde.
```

Same mechanism as the existing `IsParentNamespace = false` error: extend `EmitCrossProjectFiles`'s return type to carry diagnostics, host writes them to stderr in MSBuild format, MSBuild surfaces them as compile errors next to the user's normal build output.

### **5. Snapshot tests for the Fable emitter**

A new `Fable/` folder under the existing `Serde.FS.SourceGen.Tests` project that:

- Constructs synthetic `RpcInterfaceInfo` + `SerdeTypeInfo list` inputs covering common shapes.
- Calls `FableClientEmitter.emit` directly.
- Compares the generated string to a checked-in expected file via plain string equality.

**No `Verify`.** It works, but the dotnet diff tool requirement is friction we don't want. On mismatch the test runner writes both `<case>.expected.fs` and `<case>.actual.fs` next to the test file so the diff is a normal IDE/git operation, no global tool involved.

**Minimum coverage on first commit:**

- Record with primitive fields, in a top-level `namespace`
- Record in a sub-namespace (`module Foo.Bar = ...` parent)
- Record with `option` field
- Record with `list` field
- Record referencing another user record (cross-namespace)
- Single-case wrapper union (`type ProductId = ProductId of int`)
- Multi-case union
- Enum
- Method with `Result<T, string>` return
- Method with tupled input (`A * B -> C`)
- Interface in `module Foo.Bar` (sibling-module emission shape)
- Interface in `namespace Foo.Bar` (nested-module emission shape)

These snapshots are the regression net — every bug we've hit in the past week becomes a one-line additional test case.

### **6. Compatibility with existing samples**

`Serde.FS.Json.SampleRpc.Shared` and `Serde.FS.Json.SampleRpc.FableClient` must continue to build and the existing generated `IOrderApi.fs` output should be byte-identical (or near-identical with only cosmetic whitespace changes). The samples are the integration test for the refactor.

### **7. Update the `add-codec-factory` skill**

`.claude/skills/add-codec-factory.md` currently walks through adding runtime codec support for a new built-in type (`Dictionary`, `HashSet`, etc.) — factory implementation, registry registration, tests, and SampleRpc integration. After this refactor, supporting a new built-in type also requires a Fable-side change: a new `Kind` mapping in `synTypeToTypeInfo`, a new `FableTypeExpr` case (or extension), and emit handling in `encodeExpr` / `decodeExpr`. The skill must be extended to cover those steps so future contributors don't end up with `Dictionary` working on the server but silently broken on the Fable side.

---

### **Out of scope**

- The `RpcDispatchEmitter` (server-side dispatch + .NET RpcClient). Stays string-based — it works correctly today.
- The `[<Serde>]`-driven JSON codec generation in `JsonCodeEmitter`. Independent codepath.
- Any user-facing surface change. `[<RpcApi>]` and `[<GenerateFableClient>]` are unchanged.

---

### **Migration / breaking changes**

None for end users. Internal-only refactor inside the SourceGen and Discovery layers. Public attribute surface, generated module shapes, and consumer code patterns are all unchanged.

`RpcMethodInfo` gains optional fields. Existing `ISerdeRpcEmitter` implementations (only `JsonCodeEmitter` ships in-tree today) need a one-line update if they consume `RpcMethodInfo` directly.

---

### **Risks**

1. **`synTypeToTypeInfo` complexity.** Walking `SynType` to produce `TypeInfo` mirrors `synTypeToString`'s logic but produces structured data instead of a string. Edge cases (anonymous records, nested generics, F# function types, units of measure) need explicit handling. Mitigation: snapshot tests cover the cases we care about; everything else returns `None` and surfaces a clear MSBuild error.
2. **Type lookup gaps.** Today the parser silently fell back to a synthesized name. Under (b), an unresolved type becomes a hard build error. This is the *intent* — surface drift early — but it may light up types that were previously "working" by accident. Mitigation: run the refactor against the full debug pipeline + the BimHub test branch before merging; any newly-surfaced error is a real bug we want to know about.
3. **Test scaffolding.** Snapshot tests need a way to construct synthetic `TypeInfo` trees. SourceDjinn's `TypeInfo` is well-typed; a small helper module in the test project (`SyntheticTypes.fs`) makes this readable.

---

### **Implementation order**

1. Add `synTypeToTypeInfo` + extend `RpcMethodInfo` (no consumers yet).
2. Wire discovery to populate the new fields. Verify the existing samples still build (consumers ignoring the new fields).
3. Stand up the snapshot test scaffolding + first 2-3 tests against the *current* emitter output. (Pin existing behavior.)
4. Refactor `FableClientEmitter.emit` to use `TypeInfo`. All snapshot tests must continue to pass.
5. Delete `parseTypeString` and friends.
6. Add the rest of the snapshot tests covering shapes that previously broke.
7. Run debug pipeline + BimHub migration. Fix any new diagnostics surfaced by step 4.

Each step ships independently and leaves the build green.
