namespace Serde.FS.Json.Codec

open System

module PrimitiveCodecs =
    let boolEncoder : IJsonEncoder<bool> =
        { new IJsonEncoder<bool> with
            member _.Encode v = JsonValue.Bool v }

    let boolDecoder : IJsonDecoder<bool> =
        { new IJsonDecoder<bool> with
            member _.Decode json =
                match json with
                | JsonValue.Bool b -> b
                | _ -> failwith "Expected JSON bool" }

    let stringEncoder : IJsonEncoder<string> =
        { new IJsonEncoder<string> with
            // CLR strings can be null at interop boundaries (SQL nullable VARCHAR,
            // C# / JSON sources). Match Newtonsoft / STJ / Fable.Remoting and
            // emit JSON null instead of NREing in the writer.
            member _.Encode v =
                if isNull v then JsonValue.Null else JsonValue.String v }

    let stringDecoder : IJsonDecoder<string> =
        { new IJsonDecoder<string> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> s
                // Round-trip parity with stringEncoder: JSON null decodes back
                // to a null string reference for non-option fields.
                | JsonValue.Null -> null
                | _ -> failwith "Expected JSON string" }

    // int64, uint64 and decimal are written as JSON strings. A JSON number is
    // read as a double by JavaScript (and by many other JSON stacks), which
    // silently corrupts integers past 2^53 and decimals past 15-17 significant
    // digits. JSON numbers are still accepted on decode.

    let private invariant = Globalization.CultureInfo.InvariantCulture

    let decimalEncoder : IJsonEncoder<decimal> =
        { new IJsonEncoder<decimal> with
            member _.Encode v = JsonValue.String (v.ToString("G", invariant)) }

    let decimalDecoder : IJsonDecoder<decimal> =
        { new IJsonDecoder<decimal> with
            member _.Decode json =
                match json with
                | JsonValue.String s ->
                    // Sign, decimal point and exponent only: no whitespace,
                    // thousands separators, currency symbols or parentheses.
                    let style =
                        Globalization.NumberStyles.AllowLeadingSign
                        ||| Globalization.NumberStyles.AllowDecimalPoint
                        ||| Globalization.NumberStyles.AllowExponent
                    match Decimal.TryParse(s, style, invariant) with
                    | true, n -> n
                    | _ -> failwith $"Expected a decimal number in a JSON string, got \"%s{s}\""
                | JsonValue.Number n -> n
                | JsonValue.Float f -> decimal f
                | _ -> failwith "Expected JSON string or number (decimal)" }

    let intEncoder : IJsonEncoder<int> =
        { new IJsonEncoder<int> with
            member _.Encode v = JsonValue.Number (decimal v) }

    let intDecoder : IJsonDecoder<int> =
        { new IJsonDecoder<int> with
            member _.Decode json =
                match json with
                | JsonValue.Number n -> int n
                | JsonValue.Float f -> int (decimal f)
                | _ -> failwith "Expected JSON number" }

    let int64Encoder : IJsonEncoder<int64> =
        { new IJsonEncoder<int64> with
            member _.Encode v = JsonValue.String (v.ToString(invariant)) }

    let int64Decoder : IJsonDecoder<int64> =
        { new IJsonDecoder<int64> with
            member _.Decode json =
                match json with
                | JsonValue.String s ->
                    match Int64.TryParse(s, Globalization.NumberStyles.AllowLeadingSign, invariant) with
                    | true, n -> n
                    | _ -> failwith $"Expected an int64 in a JSON string, got \"%s{s}\""
                | JsonValue.Number n -> int64 n
                | JsonValue.Float f -> int64 (decimal f)
                | _ -> failwith "Expected JSON string or number (int64)" }

    let uint64Encoder : IJsonEncoder<uint64> =
        { new IJsonEncoder<uint64> with
            member _.Encode v = JsonValue.String (v.ToString(invariant)) }

    let uint64Decoder : IJsonDecoder<uint64> =
        { new IJsonDecoder<uint64> with
            member _.Decode json =
                match json with
                | JsonValue.String s ->
                    match UInt64.TryParse(s, Globalization.NumberStyles.None, invariant) with
                    | true, n -> n
                    | _ -> failwith $"Expected a uint64 in a JSON string, got \"%s{s}\""
                | JsonValue.Number n -> uint64 n
                | JsonValue.Float f -> uint64 (decimal f)
                | _ -> failwith "Expected JSON string or number (uint64)" }

    let floatEncoder : IJsonEncoder<float> =
        { new IJsonEncoder<float> with
            // Not `Number (decimal v)`: that conversion keeps 15 significant
            // digits and overflows outside decimal's range.
            member _.Encode v = JsonValue.Float v }

    let floatDecoder : IJsonDecoder<float> =
        { new IJsonDecoder<float> with
            member _.Decode json =
                match json with
                | JsonValue.Number n -> float n
                | JsonValue.Float f -> f
                | _ -> failwith "Expected JSON number" }

    /// Generic adapter that wraps a decode function. Used to work around the F#
    /// limitation that prevents implementing IJsonDecoder<unit> via object expression.
    type private FuncDecoder<'T>(decode: JsonValue -> obj) =
        interface IJsonDecoder<'T> with
            member _.Decode json = decode json |> unbox<'T>

    let unitEncoder : IJsonEncoder<unit> =
        { new IJsonEncoder<unit> with
            member _.Encode _ = JsonValue.Null }

    let unitDecoder : IJsonDecoder<unit> =
        FuncDecoder<unit>(fun json ->
            match json with
            | JsonValue.Null -> box ()
            | _ -> failwith "Expected JSON null")
        :> IJsonDecoder<unit>

    let byteArrayEncoder : IJsonEncoder<byte[]> =
        { new IJsonEncoder<byte[]> with
            member _.Encode bytes = JsonValue.String (Convert.ToBase64String bytes) }

    let byteArrayDecoder : IJsonDecoder<byte[]> =
        { new IJsonDecoder<byte[]> with
            member _.Decode json =
                match json with
                | JsonValue.String base64 -> Convert.FromBase64String base64
                | _ -> failwith "Expected JSON string (Base64)" }

    let guidEncoder : IJsonEncoder<Guid> =
        { new IJsonEncoder<Guid> with
            member _.Encode v = JsonValue.String (v.ToString("D")) }

    let guidDecoder : IJsonDecoder<Guid> =
        { new IJsonDecoder<Guid> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> Guid.Parse s
                | _ -> failwith "Expected JSON string (Guid)" }

    let dateTimeEncoder : IJsonEncoder<DateTime> =
        { new IJsonEncoder<DateTime> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateTimeDecoder : IJsonDecoder<DateTime> =
        { new IJsonDecoder<DateTime> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> DateTime.Parse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind)
                | _ -> failwith "Expected JSON string (DateTime)" }

    let dateTimeOffsetEncoder : IJsonEncoder<DateTimeOffset> =
        { new IJsonEncoder<DateTimeOffset> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateTimeOffsetDecoder : IJsonDecoder<DateTimeOffset> =
        { new IJsonDecoder<DateTimeOffset> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> DateTimeOffset.Parse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind)
                | _ -> failwith "Expected JSON string (DateTimeOffset)" }

    let dateOnlyEncoder : IJsonEncoder<DateOnly> =
        { new IJsonEncoder<DateOnly> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateOnlyDecoder : IJsonDecoder<DateOnly> =
        { new IJsonDecoder<DateOnly> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> DateOnly.Parse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None)
                | _ -> failwith "Expected JSON string (DateOnly)" }

    let timeOnlyEncoder : IJsonEncoder<TimeOnly> =
        { new IJsonEncoder<TimeOnly> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let timeOnlyDecoder : IJsonDecoder<TimeOnly> =
        { new IJsonDecoder<TimeOnly> with
            member _.Decode json =
                match json with
                | JsonValue.String s -> TimeOnly.Parse(s, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None)
                | _ -> failwith "Expected JSON string (TimeOnly)" }

    // -- Combined codecs --

    let boolCodec = JsonCodec.fromPair boolEncoder boolDecoder
    let stringCodec = JsonCodec.fromPair stringEncoder stringDecoder
    let decimalCodec = JsonCodec.fromPair decimalEncoder decimalDecoder
    let intCodec = JsonCodec.fromPair intEncoder intDecoder
    let int64Codec = JsonCodec.fromPair int64Encoder int64Decoder
    let uint64Codec = JsonCodec.fromPair uint64Encoder uint64Decoder
    let floatCodec = JsonCodec.fromPair floatEncoder floatDecoder
    let unitCodec = JsonCodec.fromPair unitEncoder unitDecoder
    let byteArrayCodec = JsonCodec.fromPair byteArrayEncoder byteArrayDecoder
    let guidCodec = JsonCodec.fromPair guidEncoder guidDecoder
    let dateTimeCodec = JsonCodec.fromPair dateTimeEncoder dateTimeDecoder
    let dateTimeOffsetCodec = JsonCodec.fromPair dateTimeOffsetEncoder dateTimeOffsetDecoder
    let dateOnlyCodec = JsonCodec.fromPair dateOnlyEncoder dateOnlyDecoder
    let timeOnlyCodec = JsonCodec.fromPair timeOnlyEncoder timeOnlyDecoder
