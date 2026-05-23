namespace Serde.FS.Json.Codec

/// Global mutable registry for framework and app-level registration.
module GlobalCodecRegistry =
    let mutable Current : CodecRegistry =
        CodecRegistry.withPrimitives ()
        |> CodecRegistry.addFactory (typedefof<Set<_>>, CollectionCodecs.SetCodecFactory.create)
        |> CodecRegistry.addFactory (typeof<System.Array>, CollectionCodecs.ArrayCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<list<_>>, CollectionCodecs.ListCodecFactory.create)
        // typedefof<seq<_>> = typedefof<IEnumerable<_>> — covers any value typed
        // as seq<'T> on the wire (same JSON-array shape as list/array).
        |> CodecRegistry.addFactory (typedefof<seq<_>>, CollectionCodecs.SeqCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<Map<_,_>>, CollectionCodecs.MapCodecFactory.create)
        // Option<_> factory handles non-record-field contexts (list element,
        // Result payload, Map value, tuple element, union case payload).
        |> CodecRegistry.addFactory (typedefof<Option<_>>, CollectionCodecs.OptionCodecFactory.create)
        |> CodecRegistry.addFactory (typedefof<Result<_,_>>, CollectionCodecs.ResultCodecFactory.create)
