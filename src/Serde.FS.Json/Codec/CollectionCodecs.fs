namespace Serde.FS.Json.Codec

open System
open System.Collections

module CollectionCodecs =

    // ------------------------------------------------------------------
    // Statically-typed collection codecs (Native AOT / trimming safe)
    // ------------------------------------------------------------------
    //
    // These are what the source generator emits for every closed collection
    // type it can see (e.g. `Pet list`, `Map<string, Pet>`, `Result<Order, string>`).
    // They contain no reflection: the element type is a real generic parameter,
    // so the AOT compiler sees the concrete instantiation at the generated call
    // site, and the trimmer keeps everything they touch.
    //
    // The element codec is obtained through `resolveElement` once per
    // Encode/Decode call rather than captured at construction time. This mirrors
    // the generated record codecs: registration order does not matter, and
    // codecs registered later (a second assembly's bootstrap, a user override)
    // are honored on the next call.
    //
    // The wire shape of each helper is identical to its reflection-based
    // `*CodecFactory` counterpart below, which remains as the JIT-only fallback
    // for instantiations the generator cannot see.

    /// Typed codec for `'T list`. JSON array of elements.
    let listCodecOf<'T> (resolveElement: unit -> IJsonCodec) : IJsonCodec<'T list> =
        { new IJsonCodec<'T list> with
            member _.Encode value =
                let elemCodec = resolveElement ()
                JsonValue.Array (value |> List.map (fun v -> elemCodec.Encode(box v)))
            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    let elemCodec = resolveElement ()
                    items |> List.map (fun item -> elemCodec.Decode item :?> 'T)
                | _ -> failwith $"Expected JSON array for %s{typeof<'T list>.Name}" }

    /// Typed codec for `'T[]`. JSON array of elements.
    let arrayCodecOf<'T> (resolveElement: unit -> IJsonCodec) : IJsonCodec<'T[]> =
        { new IJsonCodec<'T[]> with
            member _.Encode value =
                let elemCodec = resolveElement ()
                JsonValue.Array [ for v in value -> elemCodec.Encode(box v) ]
            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    let elemCodec = resolveElement ()
                    [| for item in items -> elemCodec.Decode item :?> 'T |]
                | _ -> failwith $"Expected JSON array for %s{typeof<'T[]>.Name}" }

    /// Typed codec for `seq<'T>`. JSON array of elements; decodes to an array
    /// exposed as seq<'T>.
    let seqCodecOf<'T> (resolveElement: unit -> IJsonCodec) : IJsonCodec<seq<'T>> =
        { new IJsonCodec<seq<'T>> with
            member _.Encode value =
                let elemCodec = resolveElement ()
                JsonValue.Array [ for v in value -> elemCodec.Encode(box v) ]
            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    let elemCodec = resolveElement ()
                    [| for item in items -> elemCodec.Decode item :?> 'T |] :> seq<'T>
                | _ -> failwith $"Expected JSON array for %s{typeof<seq<'T>>.Name}" }

    /// Typed codec for `Set<'T>`. JSON array of elements.
    let setCodecOf<'T when 'T : comparison> (resolveElement: unit -> IJsonCodec) : IJsonCodec<Set<'T>> =
        { new IJsonCodec<Set<'T>> with
            member _.Encode value =
                let elemCodec = resolveElement ()
                JsonValue.Array [ for v in value -> elemCodec.Encode(box v) ]
            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    let elemCodec = resolveElement ()
                    items |> List.map (fun item -> elemCodec.Decode item :?> 'T) |> Set.ofList
                | _ -> failwith $"Expected JSON array for %s{typeof<Set<'T>>.Name}" }

    /// Typed codec for `Map<'K, 'V>`. JSON array of `[key, value]` pairs.
    let mapCodecOf<'K, 'V when 'K : comparison> (resolveKey: unit -> IJsonCodec) (resolveValue: unit -> IJsonCodec) : IJsonCodec<Map<'K, 'V>> =
        { new IJsonCodec<Map<'K, 'V>> with
            member _.Encode value =
                let keyCodec = resolveKey ()
                let valCodec = resolveValue ()
                JsonValue.Array [
                    for KeyValue(k, v) in value ->
                        JsonValue.Array [ keyCodec.Encode(box k); valCodec.Encode(box v) ] ]
            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    let keyCodec = resolveKey ()
                    let valCodec = resolveValue ()
                    items
                    |> List.map (fun item ->
                        match item with
                        | JsonValue.Array [ k; v ] -> (keyCodec.Decode k :?> 'K), (valCodec.Decode v :?> 'V)
                        | _ -> failwith "Expected [key, value] pair in Map")
                    |> Map.ofList
                | _ -> failwith $"Expected JSON array for %s{typeof<Map<'K, 'V>>.Name}" }

    /// Typed codec for `Result<'Ok, 'Error>`. `{"Ok": ...}` or `{"Error": ...}`.
    let resultCodecOf<'Ok, 'Error> (resolveOk: unit -> IJsonCodec) (resolveError: unit -> IJsonCodec) : IJsonCodec<Result<'Ok, 'Error>> =
        { new IJsonCodec<Result<'Ok, 'Error>> with
            member _.Encode value =
                match value with
                | Ok v -> JsonValue.Object [ "Ok", (resolveOk ()).Encode(box v) ]
                | Error e -> JsonValue.Object [ "Error", (resolveError ()).Encode(box e) ]
            member _.Decode json =
                match json with
                | JsonValue.Object [ "Ok", v ] -> Ok ((resolveOk ()).Decode v :?> 'Ok)
                | JsonValue.Object [ "Error", e ] -> Error ((resolveError ()).Decode e :?> 'Error)
                | _ -> failwith $"Expected {{\"Ok\": ...}} or {{\"Error\": ...}} for %s{typeof<Result<'Ok, 'Error>>.Name}" }

    // ------------------------------------------------------------------
    // Reflection-based factories (JIT-only fallback)
    // ------------------------------------------------------------------
    //
    // Everything below builds codecs for generic instantiations that were NOT
    // visible to the source generator (runtime-only shapes such as Fable.Rpc-style
    // dynamic codec creation, or a wrapper nested inside another generic
    // definition). Under trimming / Native AOT the generator has already
    // registered a statically-typed codec for every closed type it could see, so
    // these factories are only reached for shapes that genuinely have no static
    // codec — and there they may fail (the instantiation was never compiled, or the
    // member was trimmed). CodecResolver turns such failures into a
    // SerdeCodecNotFoundException that says so.
    //
    // ILLink / ILC do not treat F# closures as compiler-generated, so the
    // reflection calls are funnelled through the named helpers in `Dyn`, each
    // carrying its suppression, instead of being sprinkled through object
    // expressions where the warnings could not be addressed.

    /// Creates a typed codec for Set<'T> given an element codec.
    let setCodec (elementCodec: IJsonCodec<'T>) : IJsonCodec<Set<'T>> =
        { new IJsonCodec<Set<'T>> with
            member _.Encode s =
                s
                |> Set.toList
                |> List.map elementCodec.Encode
                |> JsonValue.Array

            member _.Decode json =
                match json with
                | JsonValue.Array items ->
                    items
                    |> List.map elementCodec.Decode
                    |> Set.ofList
                | _ -> failwith "Expected JSON array for Set<'T>" }

    [<Literal>]
    let private FallbackJustification =
        "Reflection-based fallback for generic instantiations the source generator could not see at compile time. " +
        "Trimmed / Native AOT apps use the generator-emitted statically-typed codecs for every visible closed type; " +
        "if this path is reached there and the instantiation is unavailable, CodecResolver reports a SerdeCodecNotFoundException."

    module private Dyn =
        open System.Diagnostics.CodeAnalysis
        open Microsoft.FSharp.Reflection

        [<UnconditionalSuppressMessage("AOT", "IL3050", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2055", Justification = FallbackJustification)>]
        let makeGenericType (definition: Type) (args: Type[]) : Type = definition.MakeGenericType(args)

        [<UnconditionalSuppressMessage("AOT", "IL3050", Justification = FallbackJustification)>]
        let makeArrayType (elemType: Type) : Type = elemType.MakeArrayType()

        [<UnconditionalSuppressMessage("AOT", "IL3050", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2060", Justification = FallbackJustification)>]
        let makeGenericMethod (m: Reflection.MethodInfo) (args: Type[]) : Reflection.MethodInfo = m.MakeGenericMethod(args)

        [<UnconditionalSuppressMessage("Trimming", "IL2070", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2075", Justification = FallbackJustification)>]
        let getConstructor (ty: Type) (paramTypes: Type[]) : Reflection.ConstructorInfo = ty.GetConstructor(paramTypes)

        [<UnconditionalSuppressMessage("Trimming", "IL2070", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2075", Justification = FallbackJustification)>]
        let getMethod (ty: Type) (name: string) : Reflection.MethodInfo = ty.GetMethod(name)

        [<UnconditionalSuppressMessage("Trimming", "IL2070", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2075", Justification = FallbackJustification)>]
        let getProperty (ty: Type) (name: string) : Reflection.PropertyInfo = ty.GetProperty(name)

        [<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = FallbackJustification)>]
        let getTypeInAssembly (asm: Reflection.Assembly) (fullName: string) : Type = asm.GetType(fullName)

        [<UnconditionalSuppressMessage("AOT", "IL3050", Justification = FallbackJustification)>]
        let createArray (elemType: Type) (length: int) : Array = Array.CreateInstance(elemType, length)

        [<UnconditionalSuppressMessage("Trimming", "IL2067", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2072", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = FallbackJustification)>]
        let getUnionCases (unionType: Type) : UnionCaseInfo[] = FSharpType.GetUnionCases(unionType)

        [<UnconditionalSuppressMessage("Trimming", "IL2067", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2072", Justification = FallbackJustification)>]
        [<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = FallbackJustification)>]
        let getUnionFields (value: obj) (unionType: Type) : UnionCaseInfo * obj[] = FSharpValue.GetUnionFields(value, unionType)

        [<UnconditionalSuppressMessage("Trimming", "IL2026", Justification = FallbackJustification)>]
        let makeUnion (case: UnionCaseInfo) (fields: obj[]) : obj = FSharpValue.MakeUnion(case, fields)

        /// Materializes decoded elements into a `'T[]` for the given element type.
        let toTypedArray (elemType: Type) (items: obj list) : Array =
            let arr = createArray elemType items.Length
            items |> List.iteri (fun i item -> arr.SetValue(item, i))
            arr

    /// Factory for constructing Set<'T> codecs dynamically from the registry.
    module SetCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry

            let setType = Dyn.makeGenericType typedefof<Set<_>> [| elemType |]
            // Use the constructor: new FSharpSet<'T>(seq<'T>)
            let setOfSeqCtor = Dyn.getConstructor setType [| Dyn.makeGenericType typedefof<seq<_>> [| elemType |] |]

            { new IJsonCodec with
                member _.Type = setType
                member _.Encode obj =
                    // Set<'T> implements IEnumerable
                    let items =
                        [ for elem in (obj :?> IEnumerable) -> elemCodec.Encode elem ]
                    JsonValue.Array items
                member _.Decode json =
                    match json with
                    | JsonValue.Array items ->
                        let decoded = items |> List.map elemCodec.Decode
                        setOfSeqCtor.Invoke([| Dyn.toTypedArray elemType decoded |])
                    | _ -> failwith "Expected JSON array for Set<'T>" }

    /// Factory for constructing T[] codecs dynamically from the registry.
    module ArrayCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let arrayType = Dyn.makeArrayType elemType

            { new IJsonCodec with
                member _.Type = arrayType
                member _.Encode obj =
                    let items =
                        [ for elem in (obj :?> IEnumerable) -> elemCodec.Encode elem ]
                    JsonValue.Array items
                member _.Decode json =
                    match json with
                    | JsonValue.Array items ->
                        let decoded = items |> List.map elemCodec.Decode
                        Dyn.toTypedArray elemType decoded :> obj
                    | _ -> failwith $"Expected JSON array for %s{arrayType.Name}" }

    /// Factory for constructing seq<'T> (IEnumerable<'T>) codecs dynamically from the registry.
    /// Wire shape is identical to list/array (JSON array). On decode, returns the
    /// underlying array boxed as IEnumerable<'T> — sufficient for any seq<'T> consumer.
    module SeqCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let seqType = Dyn.makeGenericType typedefof<seq<_>> [| elemType |]

            { new IJsonCodec with
                member _.Type = seqType
                member _.Encode obj =
                    let items =
                        [ for elem in (obj :?> IEnumerable) -> elemCodec.Encode elem ]
                    JsonValue.Array items
                member _.Decode json =
                    match json with
                    | JsonValue.Array items ->
                        let decoded = items |> List.map elemCodec.Decode
                        Dyn.toTypedArray elemType decoded :> obj
                    | _ -> failwith $"Expected JSON array for %s{seqType.Name}" }

    /// Factory for constructing List<'T> codecs dynamically from the registry.
    module ListCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let listType = Dyn.makeGenericType typedefof<list<_>> [| elemType |]

            // Build an F# list from decoded elements using List.ofSeq via reflection
            let listModule = Dyn.getTypeInAssembly typedefof<list<_>>.Assembly "Microsoft.FSharp.Collections.ListModule"
            let listOfSeqMethod = Dyn.makeGenericMethod (Dyn.getMethod listModule "OfSeq") [| elemType |]

            { new IJsonCodec with
                member _.Type = listType
                member _.Encode obj =
                    let items =
                        [ for elem in (obj :?> IEnumerable) -> elemCodec.Encode elem ]
                    JsonValue.Array items
                member _.Decode json =
                    match json with
                    | JsonValue.Array items ->
                        let decoded = items |> List.map elemCodec.Decode
                        listOfSeqMethod.Invoke(null, [| Dyn.toTypedArray elemType decoded |])
                    | _ -> failwith $"Expected JSON array for %s{listType.Name}" }

    /// Factory for constructing Option<'T> codecs dynamically from the registry.
    /// Wire shape: Some x → encode inner x; None → JSON null. Matches the
    /// record-field option handling in JsonCodeEmitter, but kicks in for any
    /// non-field-level context where Option<T> needs runtime resolution
    /// (list element, Result payload, Map value, tuple element, union case
    /// payload, etc.).
    module OptionCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let innerType = typeArgs[0]
            let innerCodec = CodecResolver.resolve innerType registry
            let optionType = Dyn.makeGenericType typedefof<Option<_>> [| innerType |]

            let cases = Dyn.getUnionCases optionType
            let noneCase = cases |> Array.find (fun c -> c.Name = "None")
            let someCase = cases |> Array.find (fun c -> c.Name = "Some")

            { new IJsonCodec with
                member _.Type = optionType
                member _.Encode obj =
                    // For reference-type T, None is represented as null and
                    // GetUnionFields on a null obj throws. Defensive null check.
                    if isNull obj then
                        JsonValue.Null
                    else
                        let case, fields = Dyn.getUnionFields obj optionType
                        if case.Name = "Some" then innerCodec.Encode fields.[0]
                        else JsonValue.Null
                member _.Decode json =
                    match json with
                    | JsonValue.Null -> Dyn.makeUnion noneCase [||]
                    | _ -> Dyn.makeUnion someCase [| innerCodec.Decode json |] }

    /// Factory for constructing Result<'Ok,'Error> codecs dynamically from the registry.
    module ResultCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let okType = typeArgs[0]
            let errType = typeArgs[1]
            let okCodec = CodecResolver.resolve okType registry
            let errCodec = CodecResolver.resolve errType registry
            let resultType = Dyn.makeGenericType typedefof<Result<_,_>> [| okType; errType |]

            let tagProp = Dyn.getProperty resultType "Tag"
            let resultValueProp = Dyn.getProperty resultType "ResultValue"
            let errorValueProp = Dyn.getProperty resultType "ErrorValue"
            let newOk = Dyn.getMethod resultType "NewOk"
            let newError = Dyn.getMethod resultType "NewError"

            { new IJsonCodec with
                member _.Type = resultType
                member _.Encode obj =
                    match tagProp.GetValue(obj) :?> int with
                    | 0 -> // Ok
                        let value = resultValueProp.GetValue(obj)
                        JsonValue.Object [ "Ok", okCodec.Encode value ]
                    | _ -> // Error
                        let value = errorValueProp.GetValue(obj)
                        JsonValue.Object [ "Error", errCodec.Encode value ]
                member _.Decode json =
                    match json with
                    | JsonValue.Object [ "Ok", value ] ->
                        newOk.Invoke(null, [| okCodec.Decode value |])
                    | JsonValue.Object [ "Error", value ] ->
                        newError.Invoke(null, [| errCodec.Decode value |])
                    | _ -> failwith $"Expected {{\"Ok\": ...}} or {{\"Error\": ...}} for %s{resultType.Name}" }

    /// Factory for constructing Map<'K,'V> codecs dynamically from the registry.
    module MapCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let keyType = typeArgs[0]
            let valType = typeArgs[1]
            let keyCodec = CodecResolver.resolve keyType registry
            let valCodec = CodecResolver.resolve valType registry
            let mapType = Dyn.makeGenericType typedefof<Map<_,_>> [| keyType; valType |]

            // Build an F# Map from key-value pairs using Map.ofSeq via reflection
            let tupleType = Dyn.makeGenericType typedefof<System.Tuple<_,_>> [| keyType; valType |]
            let tupleCtor = Dyn.getConstructor tupleType [| keyType; valType |]
            let mapOfSeqMethod =
                let mapModule = Dyn.getTypeInAssembly typeof<Map<_,_>>.Assembly "Microsoft.FSharp.Collections.MapModule"
                Dyn.makeGenericMethod (Dyn.getMethod mapModule "OfSeq") [| keyType; valType |]
            // KeyValuePair<'K,'V> accessors, resolved once rather than per element.
            let kvpType = Dyn.makeGenericType typedefof<System.Collections.Generic.KeyValuePair<_,_>> [| keyType; valType |]
            let kvpKey = Dyn.getProperty kvpType "Key"
            let kvpValue = Dyn.getProperty kvpType "Value"

            { new IJsonCodec with
                member _.Type = mapType
                member _.Encode obj =
                    let items =
                        [ for kvp in (obj :?> IEnumerable) ->
                            let key = kvpKey.GetValue(kvp)
                            let value = kvpValue.GetValue(kvp)
                            JsonValue.Array [ keyCodec.Encode key; valCodec.Encode value ] ]
                    JsonValue.Array items
                member _.Decode json =
                    match json with
                    | JsonValue.Array items ->
                        let pairs =
                            items
                            |> List.map (fun item ->
                                match item with
                                | JsonValue.Array [ k; v ] ->
                                    let key = keyCodec.Decode k
                                    let value = valCodec.Decode v
                                    tupleCtor.Invoke([| key; value |])
                                | _ -> failwith "Expected [key, value] pair in Map")
                        mapOfSeqMethod.Invoke(null, [| Dyn.toTypedArray tupleType pairs |])
                    | _ -> failwith $"Expected JSON array for %s{mapType.Name}" }

    /// Reflection-based factory for a user-defined generic single-case wrapper
    /// union (`type Wrapper<'T> = Wrapper of 'T`): the payload is encoded /
    /// decoded through the inner codec, no object envelope. The source generator
    /// registers this for the open definition (`typedefof<Wrapper<_>>`) so any
    /// instantiation resolves at runtime; closed instantiations it can see are
    /// additionally emitted as statically-typed codecs (issue #14).
    let genericWrapperCodecFactory (wrapperDefinition: Type) : CodecFactory =
        fun (typeArgs: Type[]) (registry: CodecRegistry) ->
            let innerType = typeArgs.[0]
            let innerCodec = CodecResolver.resolve innerType registry
            let wrapperType = Dyn.makeGenericType wrapperDefinition typeArgs
            let caseInfo = (Dyn.getUnionCases wrapperType).[0]
            { new IJsonCodec with
                member _.Type = wrapperType
                member _.Encode obj =
                    let fields = Dyn.getUnionFields obj wrapperType |> snd
                    innerCodec.Encode(fields.[0])
                member _.Decode json =
                    let inner = innerCodec.Decode(json)
                    Dyn.makeUnion caseInfo [| inner |] }
