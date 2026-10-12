namespace Serde.FS.Json.Codec

open System
open System.Collections.Generic

/// Factory that constructs a codec for a constructed generic type.
/// Receives the type arguments and the registry (for recursive resolution).
type CodecFactory = Type[] -> CodecRegistry -> IJsonCodec

/// Immutable codec registry that maps types to codecs.
and CodecRegistry =
    { Codecs: Dictionary<Type, IJsonCodec>
      Factories: Dictionary<Type, CodecFactory> }

module CodecRegistry =
    /// Creates an empty registry.
    let empty : CodecRegistry =
        { Codecs = Dictionary<Type, IJsonCodec>()
          Factories = Dictionary<Type, CodecFactory>() }

    /// Registers a codec for the given type. Last write wins.
    let add (ty: Type, codec: IJsonCodec) (registry: CodecRegistry) : CodecRegistry =
        let newCodecs = Dictionary<Type, IJsonCodec>(registry.Codecs)
        newCodecs[ty] <- codec
        { registry with Codecs = newCodecs }

    /// Registers a factory for a generic type definition (e.g. Set<_>).
    let addFactory (genericDef: Type, factory: CodecFactory) (registry: CodecRegistry) : CodecRegistry =
        let newFactories = Dictionary<Type, CodecFactory>(registry.Factories)
        newFactories[genericDef] <- factory
        { registry with Factories = newFactories }

    /// Looks up a codec by type.
    let tryFind (ty: Type) (registry: CodecRegistry) : IJsonCodec option =
        match registry.Codecs.TryGetValue(ty) with
        | true, codec -> Some codec
        | _ -> None

    /// Looks up a factory by generic type definition.
    let tryFindFactory (genericDef: Type) (registry: CodecRegistry) : CodecFactory option =
        match registry.Factories.TryGetValue(genericDef) with
        | true, factory -> Some factory
        | _ -> None

    /// Creates a registry pre-populated with all primitive codecs.
    let withPrimitives () : CodecRegistry =
        empty
        |> add (typeof<bool>, PrimitiveCodecs.boolCodec |> JsonCodec.boxCodec)
        |> add (typeof<string>, PrimitiveCodecs.stringCodec |> JsonCodec.boxCodec)
        |> add (typeof<decimal>, PrimitiveCodecs.decimalCodec |> JsonCodec.boxCodec)
        |> add (typeof<int>, PrimitiveCodecs.intCodec |> JsonCodec.boxCodec)
        |> add (typeof<int64>, PrimitiveCodecs.int64Codec |> JsonCodec.boxCodec)
        |> add (typeof<uint64>, PrimitiveCodecs.uint64Codec |> JsonCodec.boxCodec)
        |> add (typeof<float>, PrimitiveCodecs.floatCodec |> JsonCodec.boxCodec)
        |> add (typeof<unit>, PrimitiveCodecs.unitCodec |> JsonCodec.boxCodec)
        |> add (typeof<byte[]>, PrimitiveCodecs.byteArrayCodec |> JsonCodec.boxCodec)
        |> add (typeof<Guid>, PrimitiveCodecs.guidCodec |> JsonCodec.boxCodec)
        |> add (typeof<DateTime>, PrimitiveCodecs.dateTimeCodec |> JsonCodec.boxCodec)
        |> add (typeof<DateTimeOffset>, PrimitiveCodecs.dateTimeOffsetCodec |> JsonCodec.boxCodec)
        |> add (typeof<DateOnly>, PrimitiveCodecs.dateOnlyCodec |> JsonCodec.boxCodec)
        |> add (typeof<TimeOnly>, PrimitiveCodecs.timeOnlyCodec |> JsonCodec.boxCodec)
