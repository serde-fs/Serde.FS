namespace Serde.FS.Json.Codec

open System
open System.Collections.Generic

/// Stores encoders and decoders keyed by System.Type.
type CodecRegistry() =
    let encoders = Dictionary<Type, obj>()
    let decoders = Dictionary<Type, obj>()

    /// Registers an encoder and decoder pair for type 'T.
    member _.Add<'T>(encoder: IJsonEncoder<'T>, decoder: IJsonDecoder<'T>) =
        let ty = typeof<'T>
        encoders[ty] <- box encoder
        decoders[ty] <- box decoder

    /// Retrieves the registered encoder for type 'T, if any.
    member _.TryGetEncoder<'T>() : IJsonEncoder<'T> option =
        match encoders.TryGetValue(typeof<'T>) with
        | true, enc ->
            match enc with
            | :? IJsonEncoder<'T> as e -> Some e
            | _ -> None
        | _ -> None

    /// Retrieves the registered decoder for type 'T, if any.
    member _.TryGetDecoder<'T>() : IJsonDecoder<'T> option =
        match decoders.TryGetValue(typeof<'T>) with
        | true, dec ->
            match dec with
            | :? IJsonDecoder<'T> as d -> Some d
            | _ -> None
        | _ -> None

    /// Creates an empty registry. (CodecBuilder will populate it in later specs.)
    static member Create(_types: Type list) =
        CodecRegistry()

    /// Creates a registry pre-populated with all primitive codecs.
    static member WithPrimitives() =
        let registry = CodecRegistry()
        registry.Add(PrimitiveCodecs.boolEncoder, PrimitiveCodecs.boolDecoder)
        registry.Add(PrimitiveCodecs.stringEncoder, PrimitiveCodecs.stringDecoder)
        registry.Add(PrimitiveCodecs.decimalEncoder, PrimitiveCodecs.decimalDecoder)
        registry.Add(PrimitiveCodecs.intEncoder, PrimitiveCodecs.intDecoder)
        registry.Add(PrimitiveCodecs.floatEncoder, PrimitiveCodecs.floatDecoder)
        registry.Add(PrimitiveCodecs.unitEncoder, PrimitiveCodecs.unitDecoder)
        registry.Add(PrimitiveCodecs.byteArrayEncoder, PrimitiveCodecs.byteArrayDecoder)
        registry
