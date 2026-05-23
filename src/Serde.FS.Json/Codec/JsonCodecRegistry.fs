namespace Serde.FS.Json.Codec

module JsonCodecRegistry =
    /// Creates a fresh registry with primitive codecs installed.
    let create () =
        CodecRegistry.withPrimitives ()
        |> CodecRegistry.addFactory (typedefof<Set<_>>, CollectionCodecs.SetCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<Map<_,_>>, CollectionCodecs.MapCodecFactory.create)
        |> CodecRegistry.addFactory (typeof<System.Array>, CollectionCodecs.ArrayCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<List<_>>, CollectionCodecs.ListCodecFactory.create)
        // typedefof<seq<_>> = typedefof<IEnumerable<_>> — covers any field/return
        // typed as seq<'T>, which the source generator emits as IEnumerable<'T> on
        // the CLR side.
        |> CodecRegistry.addFactory (typedefof<seq<_>>, CollectionCodecs.SeqCodecFactory.create)
        // Option<_> factory handles non-record-field contexts (list element,
        // Result payload, Map value, tuple element, union case payload).
        // Field-level options have their own special-case path in JsonCodeEmitter.
        |> CodecRegistry.addFactory (typedefof<Option<_>>, CollectionCodecs.OptionCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<Result<_,_>>, CollectionCodecs.ResultCodecFactory.create)
