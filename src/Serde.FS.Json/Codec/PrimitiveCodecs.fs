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

    /// Reads a JSON number as a whole number within [min, max]. Fractions and
    /// out-of-range values are errors rather than being truncated or wrapped.
    let private wholeNumber (typeName: string) (min: decimal) (max: decimal) (json: JsonValue) : decimal =
        let outOfRange (shown: string) = failwith $"%s{shown} is out of range for %s{typeName}"
        let notWhole (shown: string) = failwith $"Expected a whole number for %s{typeName}, got %s{shown}"
        let n =
            match json with
            | JsonValue.Number n -> n
            // Checked on the double: converting 1e-30 to decimal gives 0.
            | JsonValue.Float f when f <> Math.Truncate f -> notWhole (f.ToString("R", invariant))
            // Guard before converting: a double past decimal's range would
            // overflow, and no supported integer type is anywhere near it.
            | JsonValue.Float f when abs f < 1e20 -> decimal f
            | JsonValue.Float f -> outOfRange (f.ToString("R", invariant))
            | _ -> failwith $"Expected JSON number (%s{typeName})"
        if n <> Math.Truncate n then notWhole (n.ToString(invariant))
        elif n < min || n > max then outOfRange (n.ToString(invariant))
        else n

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
                int (wholeNumber "int" (decimal Int32.MinValue) (decimal Int32.MaxValue) json) }

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
                | _ -> int64 (wholeNumber "int64" (decimal Int64.MinValue) (decimal Int64.MaxValue) json) }

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
                | _ -> uint64 (wholeNumber "uint64" (decimal UInt64.MinValue) (decimal UInt64.MaxValue) json) }

    let private wholeNumberCodec<'T> (typeName: string) (min: decimal) (max: decimal) (toDecimal: 'T -> decimal) (ofDecimal: decimal -> 'T) : IJsonCodec<'T> =
        { new IJsonCodec<'T> with
            member _.Encode v = JsonValue.Number (toDecimal v)
            member _.Decode json = ofDecimal (wholeNumber typeName min max json) }

    let sbyteCodec : IJsonCodec<sbyte> =
        wholeNumberCodec "sbyte" (decimal SByte.MinValue) (decimal SByte.MaxValue) decimal sbyte

    let int16Codec : IJsonCodec<int16> =
        wholeNumberCodec "int16" (decimal Int16.MinValue) (decimal Int16.MaxValue) decimal int16

    let byteCodec : IJsonCodec<byte> =
        wholeNumberCodec "byte" (decimal Byte.MinValue) (decimal Byte.MaxValue) decimal byte

    let uint16Codec : IJsonCodec<uint16> =
        wholeNumberCodec "uint16" (decimal UInt16.MinValue) (decimal UInt16.MaxValue) decimal uint16

    let uint32Codec : IJsonCodec<uint32> =
        wholeNumberCodec "uint32" (decimal UInt32.MinValue) (decimal UInt32.MaxValue) decimal uint32

    let float32Codec : IJsonCodec<float32> =
        { new IJsonCodec<float32> with
            member _.Encode v =
                if Single.IsNaN v || Single.IsInfinity v then
                    // The writer rejects these with a JSON-specific error.
                    JsonValue.Float (float v)
                else
                    // Widening directly gives the double nearest the float32
                    // (0.1f -> 0.10000000149011612). Go through the shortest
                    // text that identifies the float32 so 0.1f is written 0.1.
                    JsonValue.Float (Double.Parse(v.ToString("R", invariant), invariant))
            member _.Decode json =
                let narrow (f: float) =
                    // Checked after narrowing: the shortest text for
                    // Single.MaxValue is a double slightly above it that
                    // still rounds back to Single.MaxValue.
                    let narrowed = float32 f
                    if Single.IsInfinity narrowed then
                        let shown = f.ToString("R", invariant)
                        failwith $"%s{shown} is out of range for float32"
                    else narrowed
                match json with
                | JsonValue.Number n -> narrow (float n)
                | JsonValue.Float f -> narrow f
                | _ -> failwith "Expected JSON number (float32)" }

    let charCodec : IJsonCodec<char> =
        { new IJsonCodec<char> with
            member _.Encode v = JsonValue.String (string v)
            member _.Decode json =
                match json with
                | JsonValue.String s when not (isNull s) && s.Length = 1 -> s.[0]
                | JsonValue.String _ -> failwith "Expected a one-character JSON string (char)"
                | _ -> failwith "Expected JSON string (char)" }

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
            // Null at interop boundaries (SQL nullable VARBINARY): JSON null,
            // matching stringEncoder.
            member _.Encode bytes =
                if isNull bytes then JsonValue.Null else JsonValue.String (Convert.ToBase64String bytes) }

    let byteArrayDecoder : IJsonDecoder<byte[]> =
        { new IJsonDecoder<byte[]> with
            member _.Decode json =
                match json with
                | JsonValue.String base64 -> Convert.FromBase64String base64
                | JsonValue.Null -> null
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

    // Dates decode from ISO 8601 only. `Parse` would also accept culture-shaped
    // text like "01/02/2026", whose meaning depends on who wrote it. The shape
    // check rejects what ParseExact lets through (a trailing '.', "+0200");
    // ParseExact then validates the values. [0-9] rather than \d, which also
    // matches non-ASCII digits. Fractions: .NET writes 7 digits, JS Date 3.
    let private isoShape (pattern: string) =
        Text.RegularExpressions.Regex("^" + pattern + "$", Text.RegularExpressions.RegexOptions.CultureInvariant)
    let private datePart = "[0-9]{4}-[0-9]{2}-[0-9]{2}"
    let private timePart = "[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,7})?"
    let private offsetPart = "(Z|[+-][0-9]{2}:[0-9]{2})"
    let private dateTimeShape = isoShape (datePart + "T" + timePart + offsetPart + "?")
    let private dateTimeOffsetShape = isoShape (datePart + "T" + timePart + offsetPart)
    let private dateOnlyShape = isoShape datePart
    let private timeOnlyShape = isoShape timePart

    let private isoString (typeName: string) (example: string) (shape: Text.RegularExpressions.Regex) (json: JsonValue) : string =
        match json with
        | JsonValue.String s when shape.IsMatch s -> s
        | JsonValue.String s -> failwith $"Expected an ISO 8601 %s{typeName} like \"%s{example}\", got \"%s{s}\""
        | _ -> failwith $"Expected JSON string (%s{typeName})"

    let private invalidDate (typeName: string) (s: string) : 'T =
        failwith $"\"%s{s}\" is not a valid %s{typeName}"

    let dateTimeEncoder : IJsonEncoder<DateTime> =
        { new IJsonEncoder<DateTime> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateTimeDecoder : IJsonDecoder<DateTime> =
        { new IJsonDecoder<DateTime> with
            member _.Decode json =
                let s = isoString "DateTime" "2026-03-14T10:30:05.1230000Z" dateTimeShape json
                match DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", invariant, Globalization.DateTimeStyles.RoundtripKind) with
                | true, v -> v
                | _ -> invalidDate "DateTime" s }

    let dateTimeOffsetEncoder : IJsonEncoder<DateTimeOffset> =
        { new IJsonEncoder<DateTimeOffset> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateTimeOffsetDecoder : IJsonDecoder<DateTimeOffset> =
        { new IJsonDecoder<DateTimeOffset> with
            member _.Decode json =
                let s = isoString "DateTimeOffset" "2026-03-14T10:30:05.1230000+02:00" dateTimeOffsetShape json
                let formats = [| "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"; "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" |]
                match DateTimeOffset.TryParseExact(s, formats, invariant, Globalization.DateTimeStyles.AssumeUniversal) with
                | true, v -> v
                | _ -> invalidDate "DateTimeOffset" s }

    let dateOnlyEncoder : IJsonEncoder<DateOnly> =
        { new IJsonEncoder<DateOnly> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let dateOnlyDecoder : IJsonDecoder<DateOnly> =
        { new IJsonDecoder<DateOnly> with
            member _.Decode json =
                let s = isoString "DateOnly" "2026-03-14" dateOnlyShape json
                match DateOnly.TryParseExact(s, "yyyy-MM-dd", invariant, Globalization.DateTimeStyles.None) with
                | true, v -> v
                | _ -> invalidDate "DateOnly" s }

    let timeOnlyEncoder : IJsonEncoder<TimeOnly> =
        { new IJsonEncoder<TimeOnly> with
            member _.Encode v = JsonValue.String (v.ToString("O", Globalization.CultureInfo.InvariantCulture)) }

    let timeOnlyDecoder : IJsonDecoder<TimeOnly> =
        { new IJsonDecoder<TimeOnly> with
            member _.Decode json =
                let s = isoString "TimeOnly" "10:30:05.1230000" timeOnlyShape json
                match TimeOnly.TryParseExact(s, "HH:mm:ss.FFFFFFF", invariant, Globalization.DateTimeStyles.None) with
                | true, v -> v
                | _ -> invalidDate "TimeOnly" s }

    // TimeSpan uses the .NET constant ("c") format, [-][d.]hh:mm:ss[.fffffff],
    // as System.Text.Json and Newtonsoft do. ISO 8601 durations ("PT1H") are
    // not accepted. The shape check rejects the shorter forms ParseExact "c"
    // also allows ("1", "10:30").
    let private timeSpanShape = isoShape ("-?([0-9]+\\.)?" + timePart)

    let timeSpanEncoder : IJsonEncoder<TimeSpan> =
        { new IJsonEncoder<TimeSpan> with
            member _.Encode v = JsonValue.String (v.ToString("c", invariant)) }

    let timeSpanDecoder : IJsonDecoder<TimeSpan> =
        { new IJsonDecoder<TimeSpan> with
            member _.Decode json =
                match json with
                | JsonValue.String s when timeSpanShape.IsMatch s ->
                    match TimeSpan.TryParseExact(s, "c", invariant) with
                    | true, v -> v
                    | _ -> invalidDate "TimeSpan" s
                | JsonValue.String s -> failwith $"Expected a TimeSpan like \"1.02:03:04.5000000\", got \"%s{s}\""
                | _ -> failwith "Expected JSON string (TimeSpan)" }

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
    let timeSpanCodec = JsonCodec.fromPair timeSpanEncoder timeSpanDecoder
