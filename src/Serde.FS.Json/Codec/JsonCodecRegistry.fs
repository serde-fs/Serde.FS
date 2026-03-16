namespace Serde.FS.Json.Codec

open Serde.FS

module JsonCodecRegistry =
    /// Creates a fresh registry with primitive codecs installed.
    let create () =
        CodecRegistry.withPrimitives ()