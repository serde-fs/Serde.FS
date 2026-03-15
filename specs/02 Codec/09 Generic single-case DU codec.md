### Spec F — Generic single‑case DU codec generation (`Wrapper<'T>`)

#### 1. Scope

**Goal:** Generate JSON codecs for *generic, single‑case, single‑field* DUs like:

```fsharp
[<Serde>]
type Wrapper<'T> = Wrapper of 'T
```

This must work for any `'T` that itself has a codec (generated or built‑in).

Out of scope (for now):

- Multi‑case generic DUs
- Single‑case DUs with multiple fields

---

#### 2. Detection rules in SourceGen

When walking the TypeModel:

- **Include** a DU if:
  - **IsGeneric = true**
  - Has **exactly 1 case**
  - That case has **exactly 1 field**
  - The DU is marked `[<Serde>]`

- **Skip** if:
  - More than 1 case, or
  - Case has 0 or >1 fields

This keeps Spec F focused and safe.

---

#### 3. Generated codec shape

For a DU:

```fsharp
[<Serde>]
type Wrapper<'T> = Wrapper of 'T
```

Generate something equivalent to:

```fsharp
type Wrapper_SerdeJsonCodec<'T>(inner: IJsonCodec<'T>) =
    interface IJsonCodec<Wrapper<'T>> with
        member _.Encode(Wrapper v) = inner.Encode v
        member _.Decode json = Wrapper (inner.Decode json)
```

Key points:

- **Constructor dependency:** `inner: IJsonCodec<'T>`
- **Encode:** unwrap DU → delegate to inner codec
- **Decode:** delegate to inner codec → wrap in DU

---

#### 4. Registry integration

For each such DU:

- Emit a **generic factory** registration, e.g.:

```fsharp
type Wrapper_SerdeJsonCodecFactory() =
    interface IJsonCodecFactory with
        member _.TryCreate(ty, resolver) =
            if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Wrapper<_>> then
                let tArg = ty.GetGenericArguments()[0]
                let inner = resolver.Resolve(tArg)
                let codecTy = typedefof<Wrapper_SerdeJsonCodec<_>>.MakeGenericType [| tArg |]
                Activator.CreateInstance(codecTy, inner) :?> IJsonCodec |> Some
            else
                None
```

- Ensure this factory is added to the `CodecRegistry` in the generated registration block.

This lets `Wrapper<Person>`, `Wrapper<Guid>`, etc. resolve automatically.

---

#### 5. JSON shape

By design, `Wrapper<'T>` is **transparent**:

- JSON for `Wrapper<'T>` is exactly the JSON for `'T`.
- No extra object, no tag, no wrapper structure.

Example:

```fsharp
Wrapper { Name = "Jordan" }
```

serializes as:

```json
{ "Name": "Jordan" }
```

and round‑trips via `Wrapper<Name>`.

---

#### 6. Tests / validation

Add tests (or extend SampleApp) to cover:

- `Wrapper<int>`
- `Wrapper<string>`
- `Wrapper<Name>` (record)
- `Wrapper<Person>` (complex record)
- Nested: `Wrapper<Wrapper<Name>>`

All should:

- Serialize without `SerdeCodecNotFoundException`
- Round‑trip correctly via `SerdeJson.serialize` / `SerdeJson.deserialize`.

---
