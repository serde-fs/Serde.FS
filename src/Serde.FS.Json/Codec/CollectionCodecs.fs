namespace Serde.FS.Json.Codec

open System
open System.Collections

module CollectionCodecs =

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

    /// Factory for constructing Set<'T> codecs dynamically from the registry.
    module SetCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry

            let setType = typedefof<Set<_>>.MakeGenericType(elemType)
            // FSharpSet<'T>.Create : IComparer<'T> -> seq<'T> -> FSharpSet<'T>
            let setOfSeqCtor = setType.GetConstructor([| typedefof<seq<_>>.MakeGenericType(elemType) |])
            // Use the constructor: new FSharpSet<'T>(seq<'T>)

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
                        let arr = Array.CreateInstance(elemType, decoded.Length)
                        decoded |> List.iteri (fun i item -> arr.SetValue(item, i))
                        setOfSeqCtor.Invoke([| arr |])
                    | _ -> failwith "Expected JSON array for Set<'T>" }

    /// Factory for constructing T[] codecs dynamically from the registry.
    module ArrayCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let arrayType = elemType.MakeArrayType()

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
                        let arr = Array.CreateInstance(elemType, decoded.Length)
                        decoded |> List.iteri (fun i item -> arr.SetValue(item, i))
                        arr :> obj
                    | _ -> failwith $"Expected JSON array for %s{arrayType.Name}" }

    /// Factory for constructing seq<'T> (IEnumerable<'T>) codecs dynamically from the registry.
    /// Wire shape is identical to list/array (JSON array). On decode, returns the
    /// underlying array boxed as IEnumerable<'T> — sufficient for any seq<'T> consumer.
    module SeqCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let seqType = typedefof<seq<_>>.MakeGenericType(elemType)

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
                        let arr = Array.CreateInstance(elemType, decoded.Length)
                        decoded |> List.iteri (fun i item -> arr.SetValue(item, i))
                        arr :> obj
                    | _ -> failwith $"Expected JSON array for %s{seqType.Name}" }

    /// Factory for constructing List<'T> codecs dynamically from the registry.
    module ListCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let elemType = typeArgs[0]
            let elemCodec = CodecResolver.resolve elemType registry
            let listType = typedefof<list<_>>.MakeGenericType(elemType)

            // Build an F# list from decoded elements using List.ofSeq via reflection
            let listModule = typedefof<list<_>>.Assembly.GetType("Microsoft.FSharp.Collections.ListModule")
            let listOfArrayMethod = listModule.GetMethod("OfSeq").MakeGenericMethod(elemType)

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
                        let arr = Array.CreateInstance(elemType, decoded.Length)
                        decoded |> List.iteri (fun i item -> arr.SetValue(item, i))
                        listOfArrayMethod.Invoke(null, [| arr |])
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
            let optionType = typedefof<Option<_>>.MakeGenericType(innerType)

            let cases = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(optionType)
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
                        let case, fields = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(obj, optionType)
                        if case.Name = "Some" then innerCodec.Encode fields.[0]
                        else JsonValue.Null
                member _.Decode json =
                    match json with
                    | JsonValue.Null -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(noneCase, [||])
                    | _ -> Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| innerCodec.Decode json |]) }

    /// Factory for constructing Result<'Ok,'Error> codecs dynamically from the registry.
    module ResultCodecFactory =
        let create (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
            let okType = typeArgs[0]
            let errType = typeArgs[1]
            let okCodec = CodecResolver.resolve okType registry
            let errCodec = CodecResolver.resolve errType registry
            let resultType = typedefof<Result<_,_>>.MakeGenericType(okType, errType)

            let tagProp = resultType.GetProperty("Tag")
            let resultValueProp = resultType.GetProperty("ResultValue")
            let errorValueProp = resultType.GetProperty("ErrorValue")
            let newOk = resultType.GetMethod("NewOk")
            let newError = resultType.GetMethod("NewError")

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
            let mapType = typedefof<Map<_,_>>.MakeGenericType(keyType, valType)

            // Build an F# Map from key-value pairs using Map.ofSeq via reflection
            let tupleType = typedefof<System.Tuple<_,_>>.MakeGenericType(keyType, valType)
            let tupleCtor = tupleType.GetConstructor([| keyType; valType |])
            let mapOfSeqMethod =
                typeof<Map<_,_>>.Assembly
                    .GetType("Microsoft.FSharp.Collections.MapModule")
                    .GetMethod("OfSeq")
                    .MakeGenericMethod(keyType, valType)

            { new IJsonCodec with
                member _.Type = mapType
                member _.Encode obj =
                    let items =
                        [ for kvp in (obj :?> IEnumerable) ->
                            let kvpType = kvp.GetType()
                            let key = kvpType.GetProperty("Key").GetValue(kvp)
                            let value = kvpType.GetProperty("Value").GetValue(kvp)
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
                        let arr = Array.CreateInstance(tupleType, pairs.Length)
                        pairs |> List.iteri (fun i item -> arr.SetValue(item, i))
                        mapOfSeqMethod.Invoke(null, [| arr |])
                    | _ -> failwith $"Expected JSON array for %s{mapType.Name}" }
