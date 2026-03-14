namespace Serde.FS.Json.Codec

open System
open FSharp.SourceDjinn.TypeModel.Types

/// Builds codecs from Serde metadata. (Skeleton — no encoding logic yet.)
type CodecBuilder =

    /// Builds an encoder/decoder pair for type 'T from its metadata.
    /// Not yet implemented.
    static member BuildCodec<'T>(metadata: TypeInfo) : IJsonEncoder<'T> * IJsonDecoder<'T> =
        ignore metadata
        raise (NotImplementedException "CodecBuilder.BuildCodec is not yet implemented.")

    /// Builds codecs for all given types and returns a populated registry.
    static member BuildAll(types: Type list) : CodecRegistry =
        let registry = CodecRegistry.WithPrimitives()
        for _ty in types do
            // In later specs, this will resolve TypeInfo and call BuildCodec.
            ()
        registry
