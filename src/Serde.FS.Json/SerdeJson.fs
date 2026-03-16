module Serde.FS.Json.SerdeJson

open Serde.FS
open Serde.FS.Json.Codec

/// Builds a JSON codec registry and installs it globally.
/// The `registerGenerated` function is supplied by generated code.
let registerCodecs (registerGenerated: CodecRegistry -> CodecRegistry) =
    GlobalCodecRegistry.Current <-
        JsonCodecRegistry.create ()
        |> registerGenerated

/// Installs JSON as the runtime backend (optional).
/// Only needed if the user wants Serde.Serialize/Deserialize to use JSON.
let useAsDefault (registerGenerated: CodecRegistry -> CodecRegistry) =
    // 1. Install JSON codec registry
    registerCodecs registerGenerated

    // 2. Install JSON as the runtime backend
    Serde.DefaultBackend <- Some (JsonBackend() :> ISerdeBackend)

/// Sets JSON as the default Serde backend without re-registering codecs.
/// Use this when codecs have already been auto-registered via the generated
/// `do SerdeJson.registerCodecs register` side-effect.
let setAsDefaultBackend () =
    Serde.DefaultBackend <- Some (JsonBackend() :> ISerdeBackend)

/// The global JSON backend options instance.
let options = SerdeJsonDefaults.options

/// Apply a configuration function to the global JSON backend options.
let configure (f: SerdeJsonOptions -> unit) = f options

// ---------------------------------------------------------------------------
// Codec-driven serialization / deserialization
// ---------------------------------------------------------------------------

/// Resolves the codec for 'T and encodes the value to a JsonValue.
let private encodeToJsonValue<'T> (value: 'T) : JsonValue =
    let codec = CodecResolver.resolve typeof<'T> GlobalCodecRegistry.Current
    codec.Encode(box value)

/// Resolves the codec for 'T and decodes a JsonValue to 'T.
let private decodeFromJsonValue<'T> (jsonValue: JsonValue) : 'T =
    let codec = CodecResolver.resolve typeof<'T> GlobalCodecRegistry.Current
    codec.Decode jsonValue :?> 'T

/// Codec-driven serialization of a value to a JSON string.
/// Pure Serde implementation — no System.Text.Json dependency.
let serialize<'T> (value: 'T) : string =
    let jsonValue = encodeToJsonValue<'T> value
    SerdeJsonWriter.writeToString jsonValue

/// Codec-driven serialization of a value to a UTF-8 byte array.
/// Pure Serde implementation — no System.Text.Json dependency.
let serializeToUtf8<'T> (value: 'T) : byte[] =
    let jsonValue = encodeToJsonValue<'T> value
    SerdeJsonWriter.writeToUtf8 jsonValue

/// Codec-driven deserialization of a JSON string to a value of type 'T.
/// Pure Serde implementation — no System.Text.Json dependency.
let deserialize<'T> (json: string) : 'T =
    let jsonValue = SerdeJsonReader.readFromString json
    decodeFromJsonValue<'T> jsonValue

/// Codec-driven deserialization of a UTF-8 byte array to a value of type 'T.
/// Pure Serde implementation — no System.Text.Json dependency.
let deserializeFromUtf8<'T> (bytes: byte[]) : 'T =
    let jsonValue = SerdeJsonReader.readFromUtf8 bytes
    decodeFromJsonValue<'T> jsonValue
