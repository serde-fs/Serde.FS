module Serde.FS.Json.SerdeJson

open Serde.FS
open Serde.FS.Json.Codec

/// Sets System.Text.Json as the default backend for Serde.FS.
/// Call once at application startup.
let private triggerBootstrap () =
    match global.Serde.ResolverBootstrap.registerAll with
    | Some _ -> ()
    | None ->
        let asm = System.Reflection.Assembly.GetEntryAssembly()
        if not (isNull asm) then
            match asm.GetType("Djinn.Generated.Bootstrap") with
            | null -> ()
            | ty ->
                match ty.GetMethod("init", System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static) with
                | null -> ()
                | m -> m.Invoke(null, [||]) |> ignore

let useAsDefault () =
    triggerBootstrap ()
    match global.Serde.ResolverBootstrap.registerAll with
    | Some f -> f()
    | None -> ()
    match Serde.DefaultBackend with
    | Some (:? JsonBackend) -> ()
    | _ -> Serde.DefaultBackend <- Some (JsonBackend() :> ISerdeBackend)

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
