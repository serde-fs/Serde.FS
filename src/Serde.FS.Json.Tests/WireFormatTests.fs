/// Pins the JSON wire format of the built-in codecs. Everything here is part
/// of the 1.0 contract: a failing test means the wire format changed.
module Serde.FS.Json.Tests.WireFormatTests

open System
open System.Globalization
open NUnit.Framework
open Serde.FS
open Serde.FS.Json
open Serde.FS.Json.Codec

let private roundTrip<'T> (value: 'T) : 'T =
    SerdeJson.deserialize<'T> (SerdeJson.serialize<'T> value)

let private withCulture (name: string) (f: unit -> unit) =
    let previous = CultureInfo.CurrentCulture
    CultureInfo.CurrentCulture <- CultureInfo name
    try f () finally CultureInfo.CurrentCulture <- previous

/// Cultures chosen for a decimal comma (de-DE), a non-ASCII negative sign
/// (ar-SA) and a non-Gregorian default calendar (ar-SA, th-TH).
let private cultures = [ "de-DE"; "fr-FR"; "ar-SA"; "th-TH"; "tr-TR" ]

// -- Culture independence --

[<Test>]
let ``Numbers are written the same in every culture`` () =
    for culture in cultures do
        withCulture culture (fun () ->
            Assert.That(SerdeJson.serialize 1.5, Is.EqualTo("1.5"), culture)
            Assert.That(SerdeJson.serialize -5, Is.EqualTo("-5"), culture)
            Assert.That(roundTrip 1.5, Is.EqualTo(1.5), culture)
            Assert.That(roundTrip -5, Is.EqualTo(-5), culture))

[<Test>]
let ``Dates and times round-trip in every culture`` () =
    let dateTime = DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc)
    let dateTimeOffset = DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours 2.)
    let dateOnly = DateOnly(2026, 1, 2)
    let timeOnly = TimeOnly(3, 4, 5, 678)
    let timeSpan = TimeSpan(-1, -2, -3, -4, -500)
    for culture in cultures do
        withCulture culture (fun () ->
            Assert.That(SerdeJson.serialize dateTime, Is.EqualTo("\"2026-01-02T03:04:05.6780000Z\""), culture)
            Assert.That(SerdeJson.serialize dateTimeOffset, Is.EqualTo("\"2026-01-02T03:04:05.0000000+02:00\""), culture)
            Assert.That(SerdeJson.serialize dateOnly, Is.EqualTo("\"2026-01-02\""), culture)
            Assert.That(SerdeJson.serialize timeOnly, Is.EqualTo("\"03:04:05.6780000\""), culture)
            Assert.That(SerdeJson.serialize timeSpan, Is.EqualTo("\"-1.02:03:04.5000000\""), culture)
            Assert.That(roundTrip dateTime, Is.EqualTo(dateTime), culture)
            Assert.That(roundTrip dateTimeOffset, Is.EqualTo(dateTimeOffset), culture)
            Assert.That(roundTrip dateOnly, Is.EqualTo(dateOnly), culture)
            Assert.That(roundTrip timeOnly, Is.EqualTo(timeOnly), culture)
            Assert.That(roundTrip timeSpan, Is.EqualTo(timeSpan), culture))

// -- float --

let private floatCases : float list =
    [ 0.0; 1.0; -1.0; 0.1; 1.5; 1.0 / 3.0; 2.0 / 3.0
      1.2345678901234567; 123456789012345680.0; 0.30000000000000004
      Math.PI; Math.E
      1e-30; 1e30; -1e30; 1.7e-300; 1e300
      Double.MaxValue; Double.MinValue; Double.Epsilon
      9007199254740993.0; 4.35; 5e-324 ]

[<Test>]
let ``float round-trips exactly`` () =
    for value in floatCases do
        let json = SerdeJson.serialize value
        Assert.That(SerdeJson.deserialize<float> json, Is.EqualTo(value), json)

[<Test>]
let ``float round-trips exactly inside a container`` () =
    let values = floatCases
    Assert.That(roundTrip values, Is.EqualTo(values))
    let optional = floatCases |> List.map Some
    Assert.That(roundTrip optional, Is.EqualTo(optional))

[<Test>]
let ``float wire shape`` () =
    Assert.That(SerdeJson.serialize 0.0, Is.EqualTo("0"))
    Assert.That(SerdeJson.serialize 1.0, Is.EqualTo("1"))
    Assert.That(SerdeJson.serialize 0.1, Is.EqualTo("0.1"))
    Assert.That(SerdeJson.serialize -2.5, Is.EqualTo("-2.5"))
    Assert.That(SerdeJson.serialize 1e30, Is.EqualTo("1E+30"))
    Assert.That(SerdeJson.serialize 1e-30, Is.EqualTo("1E-30"))

[<Test>]
let ``float decodes any JSON number form`` () =
    Assert.That(SerdeJson.deserialize<float> "42", Is.EqualTo(42.0))
    Assert.That(SerdeJson.deserialize<float> "-0.5", Is.EqualTo(-0.5))
    Assert.That(SerdeJson.deserialize<float> "1e3", Is.EqualTo(1000.0))
    Assert.That(SerdeJson.deserialize<float> "1.5E-3", Is.EqualTo(0.0015))
    Assert.That(SerdeJson.deserialize<float> "1e+30", Is.EqualTo(1e30))

[<Test>]
let ``float NaN and infinities are rejected on encode`` () =
    for value in [ Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity ] do
        let ex = Assert.Throws<SerdeJsonException>(fun () -> SerdeJson.serialize value |> ignore)
        Assert.That(ex.Message, Does.Contain("JSON"))

[<Test>]
let ``Number too large for a float is a parse error`` () =
    Assert.Throws<SerdeJsonParseException>(fun () -> SerdeJson.deserialize<float> "1e400" |> ignore) |> ignore

// -- int --

[<Test>]
let ``int is a JSON number`` () =
    Assert.That(SerdeJson.serialize 42, Is.EqualTo("42"))
    Assert.That(SerdeJson.serialize Int32.MinValue, Is.EqualTo("-2147483648"))
    Assert.That(roundTrip Int32.MaxValue, Is.EqualTo(Int32.MaxValue))

// -- int64 / uint64 / decimal --
//
// Written as JSON strings: a JavaScript client reads JSON numbers as doubles,
// which silently corrupts anything past 2^53 or 15-17 significant digits.
// Numbers are still accepted on decode.

[<Test>]
let ``int64 uint64 and decimal are written as JSON strings`` () =
    Assert.That(SerdeJson.serialize 42L, Is.EqualTo("\"42\""))
    Assert.That(SerdeJson.serialize -42L, Is.EqualTo("\"-42\""))
    Assert.That(SerdeJson.serialize Int64.MaxValue, Is.EqualTo("\"9223372036854775807\""))
    Assert.That(SerdeJson.serialize Int64.MinValue, Is.EqualTo("\"-9223372036854775808\""))
    Assert.That(SerdeJson.serialize 42UL, Is.EqualTo("\"42\""))
    Assert.That(SerdeJson.serialize UInt64.MaxValue, Is.EqualTo("\"18446744073709551615\""))
    Assert.That(SerdeJson.serialize 1.50m, Is.EqualTo("\"1.50\""))
    Assert.That(SerdeJson.serialize -0.1m, Is.EqualTo("\"-0.1\""))
    Assert.That(SerdeJson.serialize Decimal.MaxValue, Is.EqualTo("\"79228162514264337593543950335\""))

[<Test>]
let ``int64 uint64 and decimal round-trip at their limits`` () =
    Assert.That(roundTrip 9007199254740993L, Is.EqualTo(9007199254740993L))
    Assert.That(roundTrip Int64.MaxValue, Is.EqualTo(Int64.MaxValue))
    Assert.That(roundTrip Int64.MinValue, Is.EqualTo(Int64.MinValue))
    Assert.That(roundTrip Decimal.MaxValue, Is.EqualTo(Decimal.MaxValue))
    Assert.That(roundTrip 0.0000000000000000000000000001m, Is.EqualTo(0.0000000000000000000000000001m))
    Assert.That(roundTrip Decimal.MinValue, Is.EqualTo(Decimal.MinValue))
    Assert.That(roundTrip UInt64.MaxValue, Is.EqualTo(UInt64.MaxValue))
    Assert.That(roundTrip 0UL, Is.EqualTo(0UL))
    let nested = [ Some Int64.MaxValue; None; Some 0L ]
    Assert.That(roundTrip nested, Is.EqualTo(nested))
    let keyed = Map.ofList [ Int64.MinValue, 1.50m; 7L, Decimal.MaxValue ]
    Assert.That(roundTrip keyed, Is.EqualTo(keyed))

[<Test>]
let ``int64 uint64 and decimal still decode JSON numbers`` () =
    Assert.That(SerdeJson.deserialize<int64> "42", Is.EqualTo(42L))
    Assert.That(SerdeJson.deserialize<int64> "-9223372036854775808", Is.EqualTo(Int64.MinValue))
    Assert.That(SerdeJson.deserialize<uint64> "18446744073709551615", Is.EqualTo(UInt64.MaxValue))
    Assert.That(SerdeJson.deserialize<decimal> "1.50", Is.EqualTo(1.50m))
    Assert.That(SerdeJson.deserialize<decimal> "-3", Is.EqualTo(-3m))

[<Test>]
let ``decimal string decodes exponent notation`` () =
    // A JavaScript decimal library may print very large or small values
    // this way.
    Assert.That(SerdeJson.deserialize<decimal> "\"1.5e+3\"", Is.EqualTo(1500m))
    Assert.That(SerdeJson.deserialize<decimal> "\"1E-7\"", Is.EqualTo(0.0000001m))

let private rejects<'T> (json: string) =
    Assert.That((fun () -> SerdeJson.deserialize<'T> json |> ignore), Throws.Exception, json)

[<Test>]
let ``Malformed numeric strings are rejected`` () =
    for json in [ "\"\""; "\"abc\""; "\" 42\""; "\"42 \""; "\"4 2\""; "\"1,000\""; "\"0x1F\""; "\"1.5\""; "\"1e3\""; "\"9223372036854775808\""; "true"; "null" ] do
        rejects<int64> json
    for json in [ "\"\""; "\"abc\""; "\"-1\""; "\" 42\""; "\"1,000\""; "\"1.5\""; "\"18446744073709551616\""; "-1"; "null" ] do
        rejects<uint64> json
    for json in [ "\"\""; "\"abc\""; "\" 1.5\""; "\"1,5\""; "\"1,000.5\""; "\"$5\""; "\"(5)\""; "\"NaN\""; "true"; "null" ] do
        rejects<decimal> json

[<Test>]
let ``Numeric strings are written and read the same in every culture`` () =
    for culture in cultures do
        withCulture culture (fun () ->
            Assert.That(SerdeJson.serialize -9000000000L, Is.EqualTo("\"-9000000000\""), culture)
            Assert.That(SerdeJson.serialize 1234.5m, Is.EqualTo("\"1234.5\""), culture)
            Assert.That(roundTrip -9000000000L, Is.EqualTo(-9000000000L), culture)
            Assert.That(roundTrip 1234.5m, Is.EqualTo(1234.5m), culture)
            Assert.That(roundTrip UInt64.MaxValue, Is.EqualTo(UInt64.MaxValue), culture))

[<Test>]
let ``Integers and decimals still decode exponent notation`` () =
    Assert.That(SerdeJson.deserialize<int> "1e2", Is.EqualTo(100))
    Assert.That(SerdeJson.deserialize<int64> "1E3", Is.EqualTo(1000L))
    Assert.That(SerdeJson.deserialize<decimal> "1.5e1", Is.EqualTo(15m))

// -- Small integers --

[<Test>]
let ``Small integer types are JSON numbers and round-trip at their limits`` () =
    Assert.That(SerdeJson.serialize 5y, Is.EqualTo("5"))
    Assert.That(SerdeJson.serialize -5s, Is.EqualTo("-5"))
    Assert.That(SerdeJson.serialize 5uy, Is.EqualTo("5"))
    Assert.That(SerdeJson.serialize 5us, Is.EqualTo("5"))
    Assert.That(SerdeJson.serialize 5u, Is.EqualTo("5"))
    Assert.That(SerdeJson.serialize UInt32.MaxValue, Is.EqualTo("4294967295"))
    for v in [ SByte.MinValue; 0y; SByte.MaxValue ] do Assert.That(roundTrip v, Is.EqualTo(v))
    for v in [ Int16.MinValue; 0s; Int16.MaxValue ] do Assert.That(roundTrip v, Is.EqualTo(v))
    for v in [ Byte.MinValue; Byte.MaxValue ] do Assert.That(roundTrip v, Is.EqualTo(v))
    for v in [ UInt16.MinValue; UInt16.MaxValue ] do Assert.That(roundTrip v, Is.EqualTo(v))
    for v in [ UInt32.MinValue; UInt32.MaxValue ] do Assert.That(roundTrip v, Is.EqualTo(v))

[<Test>]
let ``Small integer types reject out-of-range fractional and non-numeric input`` () =
    for json in [ "128"; "-129"; "1.5"; "\"5\""; "null"; "true"; "1e40" ] do rejects<sbyte> json
    for json in [ "32768"; "-32769"; "1.5"; "\"5\""; "null" ] do rejects<int16> json
    for json in [ "256"; "-1"; "1.5"; "\"5\""; "null" ] do rejects<byte> json
    for json in [ "65536"; "-1"; "0.5"; "\"5\""; "null" ] do rejects<uint16> json
    for json in [ "4294967296"; "-1"; "0.5"; "\"5\""; "null"; "1e40" ] do rejects<uint32> json

[<Test>]
let ``Small integer types accept whole numbers in any number form`` () =
    Assert.That(SerdeJson.deserialize<int16> "1e2", Is.EqualTo(100s))
    Assert.That(SerdeJson.deserialize<byte> "2.0", Is.EqualTo(2uy))
    Assert.That(SerdeJson.deserialize<uint32> "4.294967295E9", Is.EqualTo(UInt32.MaxValue))

[<Test>]
let ``byte array stays base64 while other byte collections are number arrays`` () =
    Assert.That(SerdeJson.serialize [| 1uy; 2uy |], Is.EqualTo("\"AQI=\""))
    Assert.That(SerdeJson.serialize [ 1uy; 2uy ], Is.EqualTo("[1,2]"))
    Assert.That(roundTrip [ 0uy; 255uy ], Is.EqualTo([ 0uy; 255uy ]))

// -- float32 --

[<Test>]
let ``float32 is written in its shortest form`` () =
    Assert.That(SerdeJson.serialize 0.1f, Is.EqualTo("0.1"))
    Assert.That(SerdeJson.serialize 1.5f, Is.EqualTo("1.5"))
    Assert.That(SerdeJson.serialize -2f, Is.EqualTo("-2"))
    Assert.That(SerdeJson.serialize 16777216f, Is.EqualTo("16777216"))

[<Test>]
let ``float32 round-trips exactly`` () =
    let edge = [ 0f; 0.1f; 1f / 3f; Single.MaxValue; Single.MinValue; Single.Epsilon; 1e-30f; 1e30f; 1.17549435e-38f ]
    for v in edge do
        let json = SerdeJson.serialize v
        Assert.That(SerdeJson.deserialize<float32> json, Is.EqualTo(v), json)
    // Every bit pattern is a distinct float32; sample them broadly.
    let rng = Random(20261012)
    let mutable checkedCount = 0
    while checkedCount < 200000 do
        let v = BitConverter.Int32BitsToSingle(rng.Next(Int32.MinValue, Int32.MaxValue))
        if not (Single.IsNaN v) && not (Single.IsInfinity v) then
            checkedCount <- checkedCount + 1
            let back = SerdeJson.deserialize<float32> (SerdeJson.serialize v)
            if back <> v then
                Assert.Fail(sprintf "float32 %s (bits %08x) decoded as %s" (v.ToString "R") (BitConverter.SingleToInt32Bits v) (back.ToString "R"))

[<Test>]
let ``float32 rejects NaN infinities and out-of-range input`` () =
    for value in [ Single.NaN; Single.PositiveInfinity; Single.NegativeInfinity ] do
        Assert.Throws<SerdeJsonException>(fun () -> SerdeJson.serialize value |> ignore) |> ignore
    for json in [ "1e39"; "-1e39"; "\"1.5\""; "null" ] do rejects<float32> json
    Assert.That(SerdeJson.deserialize<float32> "42", Is.EqualTo(42f))
    // The Fable client sends a float32 widened to a double.
    Assert.That(SerdeJson.deserialize<float32> "0.10000000149011612", Is.EqualTo(0.1f))

// -- char --

[<Test>]
let ``char is a one-character JSON string`` () =
    Assert.That(SerdeJson.serialize 'x', Is.EqualTo("\"x\""))
    Assert.That(SerdeJson.serialize '"', Is.EqualTo("\"\\\"\""))
    Assert.That(SerdeJson.serialize '\n', Is.EqualTo("\"\\n\""))
    Assert.That(SerdeJson.serialize 'é', Is.EqualTo("\"é\""))
    for c in [ 'x'; ' '; '"'; '\\'; '\n'; '\000'; 'é'; '\u2028'; '\uFFFF' ] do
        Assert.That(roundTrip c, Is.EqualTo(c), sprintf "U+%04X" (int c))

[<Test>]
let ``char rejects anything but a one-character string`` () =
    for json in [ "\"\""; "\"ab\""; "120"; "null"; "true"; "[\"x\"]" ] do rejects<char> json

[<Test>]
let ``Unpaired surrogates survive both the string and the UTF-8 path`` () =
    // UTF-8 cannot carry half of a surrogate pair, so the writer has to
    // escape it; written raw it would be replaced with U+FFFD.
    let high, low = '\uD83D', '\uDE00'
    Assert.That(SerdeJson.serialize high, Is.EqualTo("\"\\ud83d\""))
    for c in [ high; low ] do
        Assert.That(roundTrip c, Is.EqualTo(c))
        Assert.That(SerdeJson.deserializeFromUtf8<char> (SerdeJson.serializeToUtf8 c), Is.EqualTo(c))
    let lonely = "a" + string high + "b" + string low
    Assert.That(SerdeJson.deserializeFromUtf8<string> (SerdeJson.serializeToUtf8 lonely), Is.EqualTo(lonely))
    // A proper pair is left alone.
    let paired = string high + string low
    Assert.That(SerdeJson.serialize paired, Is.EqualTo("\"" + paired + "\""))
    Assert.That(SerdeJson.deserializeFromUtf8<string> (SerdeJson.serializeToUtf8 paired), Is.EqualTo(paired))

// -- Whole-number strictness for int / int64 / uint64 number input --

[<Test>]
let ``int rejects fractional out-of-range and non-numeric input`` () =
    for json in [ "1.9"; "-0.5"; "1e-1"; "1e-30"; "2147483648"; "-2147483649"; "3000000000"; "1e40"; "\"5\""; "null"; "true" ] do
        rejects<int> json

[<Test>]
let ``int accepts whole numbers in any number form`` () =
    Assert.That(SerdeJson.deserialize<int> "2147483647", Is.EqualTo(Int32.MaxValue))
    Assert.That(SerdeJson.deserialize<int> "-2147483648", Is.EqualTo(Int32.MinValue))
    Assert.That(SerdeJson.deserialize<int> "2.0", Is.EqualTo(2))
    Assert.That(SerdeJson.deserialize<int> "1e2", Is.EqualTo(100))
    Assert.That(SerdeJson.deserialize<int> "-0", Is.EqualTo(0))

[<Test>]
let ``int64 and uint64 number input must be whole and in range`` () =
    for json in [ "1.5"; "9223372036854775808"; "-9223372036854775809"; "1e40" ] do rejects<int64> json
    for json in [ "1.5"; "-1"; "18446744073709551616"; "1e40" ] do rejects<uint64> json
    Assert.That(SerdeJson.deserialize<int64> "9223372036854775807", Is.EqualTo(Int64.MaxValue))
    Assert.That(SerdeJson.deserialize<int64> "3.0", Is.EqualTo(3L))
    Assert.That(SerdeJson.deserialize<uint64> "1e3", Is.EqualTo(1000UL))

[<Test>]
let ``Whole-number errors say what was wrong`` () =
    let messageOf (f: unit -> unit) = (Assert.Catch(fun () -> f ())).Message
    Assert.That(messageOf (fun () -> SerdeJson.deserialize<int> "1.9" |> ignore), Does.Contain("whole number").And.Contain("1.9"))
    Assert.That(messageOf (fun () -> SerdeJson.deserialize<int> "3000000000" |> ignore), Does.Contain("out of range").And.Contain("3000000000"))
    Assert.That(messageOf (fun () -> SerdeJson.deserialize<int64> "1.5" |> ignore), Does.Contain("whole number"))

// -- Dates: ISO 8601 only --

let private str (s: string) = "\"" + s + "\""

[<Test>]
let ``DateTime accepts ISO 8601 as written by .NET and Fable`` () =
    let utc = DateTime(2026, 3, 14, 10, 30, 5, 123, DateTimeKind.Utc)
    // .NET writes 7 fractional digits; Fable (JS Date) writes 3.
    for s in [ "2026-03-14T10:30:05.1230000Z"; "2026-03-14T10:30:05.123Z" ] do
        let v = SerdeJson.deserialize<DateTime> (str s)
        Assert.That(v, Is.EqualTo(utc), s)
        Assert.That(v.Kind, Is.EqualTo(DateTimeKind.Utc), s)
    let unspecified = SerdeJson.deserialize<DateTime> (str "2026-03-14T10:30:05.123")
    Assert.That(unspecified, Is.EqualTo(DateTime(2026, 3, 14, 10, 30, 5, 123)))
    Assert.That(unspecified.Kind, Is.EqualTo(DateTimeKind.Unspecified))
    Assert.That(SerdeJson.deserialize<DateTime> (str "2026-03-14T10:30:05Z"), Is.EqualTo(DateTime(2026, 3, 14, 10, 30, 5, DateTimeKind.Utc)))
    let withOffset = SerdeJson.deserialize<DateTime> (str "2026-03-14T10:30:05.123-04:00")
    Assert.That(withOffset.ToUniversalTime(), Is.EqualTo(DateTime(2026, 3, 14, 14, 30, 5, 123, DateTimeKind.Utc)))

[<Test>]
let ``DateTime rejects non-ISO strings`` () =
    for s in [ "01/02/2026"; "March 14 2026"; "2026-03-14 10:30:05"; "2026-3-14T10:30:05"; "2026-03-14"
               "2026-03-14T10:30Z"; "2026-03-14T10:30:05."; "2026-03-14T10:30:05.Z"; "2026-03-14T10:30:05.12345678Z"
               "2026-03-14T10:30:05+0200"; "2026-03-14T10:30:05+02"; " 2026-03-14T10:30:05Z"; "2026-03-14T10:30:05Z "
               "2026-13-14T10:30:05Z"; "2026-03-14T24:00:00Z"; "２０２６-03-14T10:30:05Z"; "" ] do
        rejects<DateTime> (str s)

[<Test>]
let ``DateTimeOffset accepts ISO 8601 with an offset`` () =
    let expected = DateTimeOffset(2026, 3, 14, 10, 30, 5, 123, TimeSpan.FromHours 2.0)
    for s in [ "2026-03-14T10:30:05.1230000+02:00"; "2026-03-14T10:30:05.123+02:00" ] do
        let v = SerdeJson.deserialize<DateTimeOffset> (str s)
        Assert.That(v, Is.EqualTo(expected), s)
        Assert.That(v.Offset, Is.EqualTo(TimeSpan.FromHours 2.0), s)
    let z = SerdeJson.deserialize<DateTimeOffset> (str "2026-03-14T10:30:05Z")
    Assert.That(z.Offset, Is.EqualTo(TimeSpan.Zero))
    Assert.That(z.UtcDateTime, Is.EqualTo(DateTime(2026, 3, 14, 10, 30, 5, DateTimeKind.Utc)))

[<Test>]
let ``DateTimeOffset rejects strings without an offset or not ISO`` () =
    // Without an offset the instant would depend on the server's time zone.
    for s in [ "2026-03-14T10:30:05"; "2026-03-14T10:30:05.123"; "03/14/2026 10:30:05 +02:00"; "2026-03-14T10:30:05+0200"
               "2026-03-14T10:30:05.+02:00"; "2026-03-14" ] do
        rejects<DateTimeOffset> (str s)

[<Test>]
let ``DateOnly accepts only yyyy-MM-dd`` () =
    Assert.That(SerdeJson.deserialize<DateOnly> (str "2026-03-14"), Is.EqualTo(DateOnly(2026, 3, 14)))
    for s in [ "03/14/2026"; "14.03.2026"; "2026-3-14"; "20260314"; "2026-03-14T00:00:00"; "2026-02-30"; "March 14, 2026" ] do
        rejects<DateOnly> (str s)

[<Test>]
let ``TimeOnly accepts only HH:mm:ss with optional fraction`` () =
    for s in [ "10:30:05.1230000"; "10:30:05.123" ] do
        Assert.That(SerdeJson.deserialize<TimeOnly> (str s), Is.EqualTo(TimeOnly(10, 30, 5, 123)), s)
    Assert.That(SerdeJson.deserialize<TimeOnly> (str "10:30:05"), Is.EqualTo(TimeOnly(10, 30, 5)))
    for s in [ "10:30"; "10:30 AM"; "1:30:05"; "10:30:05."; "25:00:00"; "10:30:05Z"; "10:30:05.12345678" ] do
        rejects<TimeOnly> (str s)

[<Test>]
let ``TimeSpan is written in the constant format`` () =
    Assert.That(SerdeJson.serialize TimeSpan.Zero, Is.EqualTo(str "00:00:00"))
    Assert.That(SerdeJson.serialize (TimeSpan.FromMinutes 90.0), Is.EqualTo(str "01:30:00"))
    Assert.That(SerdeJson.serialize (TimeSpan(1, 2, 3, 4, 500)), Is.EqualTo(str "1.02:03:04.5000000"))
    Assert.That(SerdeJson.serialize (TimeSpan.FromTicks 1L), Is.EqualTo(str "00:00:00.0000001"))
    for v in [ TimeSpan.Zero; TimeSpan.FromTicks 1L; TimeSpan.FromHours -1.5; TimeSpan(10000, 23, 59, 59, 999)
               TimeSpan.MaxValue; TimeSpan.MinValue ] do
        Assert.That(roundTrip v, Is.EqualTo(v), string v)

[<Test>]
let ``TimeSpan accepts the constant format as written by .NET and Fable`` () =
    Assert.That(SerdeJson.deserialize<TimeSpan> (str "1.02:03:04.5000000"), Is.EqualTo(TimeSpan(1, 2, 3, 4, 500)))
    Assert.That(SerdeJson.deserialize<TimeSpan> (str "1.02:03:04.5"), Is.EqualTo(TimeSpan(1, 2, 3, 4, 500)))
    Assert.That(SerdeJson.deserialize<TimeSpan> (str "-01:30:00"), Is.EqualTo(TimeSpan.FromHours -1.5))
    Assert.That(SerdeJson.deserialize<TimeSpan> (str "00:00:00"), Is.EqualTo(TimeSpan.Zero))

[<Test>]
let ``TimeSpan rejects other formats`` () =
    // "1" and "10:30" are valid for ParseExact "c" but not what we write;
    // "1:02:03:04" is the general ("g") format; ISO 8601 durations are not accepted.
    for s in [ "1"; "10:30"; "1:02:03:04"; "1.2:03:04"; "PT1H30M"; "P1D"; "24:00:00"; "00:60:00"; "00:00:60"
               "00:00:00."; "00:00:00.12345678"; "+01:00:00"; " 01:00:00"; "01:00:00 "; "1.02:03:04,5"; "" ] do
        rejects<TimeSpan> (str s)
    rejects<TimeSpan> "5400000"
    let ex = Assert.Catch(fun () -> SerdeJson.deserialize<TimeSpan> (str "PT1H") |> ignore)
    Assert.That(ex.Message, Does.Contain("TimeSpan").And.Contain("PT1H"))

// -- Guid --

[<Test>]
let ``Guid is the lower-case hyphenated form`` () =
    let g = Guid.Parse "12345678-1234-1234-1234-123456789ABC"
    Assert.That(SerdeJson.serialize g, Is.EqualTo(str "12345678-1234-1234-1234-123456789abc"))
    for s in [ "12345678-1234-1234-1234-123456789abc"; "12345678-1234-1234-1234-123456789ABC" ] do
        Assert.That(SerdeJson.deserialize<Guid> (str s), Is.EqualTo(g), s)

[<Test>]
let ``Guid rejects every other form`` () =
    for s in [ "{12345678-1234-1234-1234-123456789abc}"; "(12345678-1234-1234-1234-123456789abc)"
               "12345678123412341234123456789abc"; " 12345678-1234-1234-1234-123456789abc"
               "12345678-1234-1234-1234-123456789abc "; "12345678-1234-1234-1234-123456789ab"
               "{0x12345678,0x1234,0x1234,{0x12,0x34,0x12,0x34,0x56,0x78,0x9a,0xbc}}"
               "1234567g-1234-1234-1234-123456789abc"; "" ] do
        rejects<Guid> (str s)
    let ex = Assert.Catch(fun () -> SerdeJson.deserialize<Guid> (str "{12345678-1234-1234-1234-123456789abc}") |> ignore)
    Assert.That(ex.Message, Does.Contain("Guid").And.Contain("{12345678"))

[<Test>]
let ``Date errors name the type and the expected format`` () =
    let ex = Assert.Catch(fun () -> SerdeJson.deserialize<DateTime> (str "01/02/2026") |> ignore)
    Assert.That(ex.Message, Does.Contain("DateTime").And.Contain("ISO 8601").And.Contain("01/02/2026"))

// -- Nested options: Some wraps only when the inner value can itself be null --

[<Test>]
let ``Single option is unchanged on the wire`` () =
    Assert.That(SerdeJson.serialize (Some 5), Is.EqualTo("5"))
    Assert.That(SerdeJson.serialize (None: int option), Is.EqualTo("null"))
    Assert.That(SerdeJson.deserialize<int option> "5", Is.EqualTo(Some 5))

[<Test>]
let ``Nested option wraps Some so Some None survives`` () =
    Assert.That(SerdeJson.serialize (Some (None: int option)), Is.EqualTo("""{"Some":null}"""))
    Assert.That(SerdeJson.serialize (Some (Some 5)), Is.EqualTo("""{"Some":5}"""))
    Assert.That(SerdeJson.serialize (None: int option option), Is.EqualTo("null"))
    Assert.That(SerdeJson.serialize (Some (Some (None: int option))), Is.EqualTo("""{"Some":{"Some":null}}"""))
    for v in [ None; Some None; Some (Some 5) ] do
        Assert.That(roundTrip v, Is.EqualTo(v), sprintf "%A" v)
    for v in [ None; Some None; Some (Some None); Some (Some (Some "x")) ] do
        Assert.That(roundTrip v, Is.EqualTo(v), sprintf "%A" v)

[<Test>]
let ``unit option wraps Some because unit is null on the wire`` () =
    Assert.That(SerdeJson.serialize (Some ()), Is.EqualTo("""{"Some":null}"""))
    Assert.That(roundTrip (Some ()), Is.EqualTo(Some ()))
    Assert.That(roundTrip (None: unit option), Is.EqualTo(None: unit option))

[<Test>]
let ``Nested options round-trip inside collections`` () =
    let values = [ Some None; None; Some (Some 1) ]
    Assert.That(SerdeJson.serialize values, Is.EqualTo("""[{"Some":null},null,{"Some":1}]"""))
    Assert.That(roundTrip values, Is.EqualTo(values))
    let m = Map [ "a", Some (None: string option); "b", None ]
    Assert.That(roundTrip m, Is.EqualTo(m))

[<Test>]
let ``Nested option decode requires null or the Some wrapper`` () =
    for json in [ "5"; "\"x\""; "[5]"; """{"Other":5}"""; """{"Some":5,"Extra":1}"""; "{}" ] do
        rejects<int option option> json
    let ex = Assert.Catch(fun () -> SerdeJson.deserialize<int option option> "5" |> ignore)
    Assert.That(ex.Message, Does.Contain("Some"))

// -- Null references: Some null is sent as None; bare null stays null --
// Values from SQL / C# interop can be null where F# says they cannot. A
// `Some null` (Some instead of Option.ofObj) is normalised to None, exactly
// as Option.ofObj would have; a bare null round-trips as null, like string.

[<Test>]
let ``Some null string is sent as None`` () =
    Assert.That(SerdeJson.serialize (Some (null: string)), Is.EqualTo("null"))
    Assert.That(roundTrip (Some (null: string)), Is.EqualTo(None: string option))
    Assert.That(roundTrip (Some (Some (null: string))), Is.EqualTo(Some (None: string option)))
    Assert.That(roundTrip [ Some "a"; Some null; None ], Is.EqualTo([ Some "a"; None; None ]))

[<Test>]
let ``Null byte array, array and seq encode as null`` () =
    Assert.That(SerdeJson.serialize (null: byte[]), Is.EqualTo("null"))
    Assert.That(SerdeJson.serialize (null: int[]), Is.EqualTo("null"))
    Assert.That(SerdeJson.serialize (null: string[]), Is.EqualTo("null"))
    Assert.That(SerdeJson.serialize (null: seq<int>), Is.EqualTo("null"))
    Assert.That(SerdeJson.deserialize<byte[]> "null", Is.Null)
    Assert.That(SerdeJson.deserialize<int[]> "null", Is.Null)
    Assert.That(SerdeJson.deserialize<seq<int>> "null", Is.Null)

[<Test>]
let ``Some null byte array, array and seq are sent as None`` () =
    Assert.That(roundTrip (Some (null: byte[])), Is.EqualTo(None: byte[] option))
    Assert.That(roundTrip (Some (null: int[])), Is.EqualTo(None: int[] option))
    Assert.That(roundTrip (Some (null: seq<int>)) |> Option.isNone, Is.True)
    Assert.That(roundTrip (Some [| 1uy; 2uy |]), Is.EqualTo(Some [| 1uy; 2uy |]))

[<Test>]
let ``Null elements inside arrays and maps round-trip`` () =
    Assert.That(roundTrip [| null; [| 1uy |] |], Is.EqualTo([| null; [| 1uy |] |]))
    let m = Map [ "a", (null: int[]); "b", [| 1 |] ]
    Assert.That(roundTrip m, Is.EqualTo(m))

// -- Decode errors name the type and field --
// The messages are not part of the wire format, but they must say what was
// wrong instead of surfacing a KeyNotFoundException or IndexOutOfRangeException.

[<Test>]
let ``A missing field names the field and type`` () =
    let fieldMap = dict [ "Name", JsonValue.String "x" ]
    let ex = Assert.Catch(fun () -> DecodeWire.field "App.Person" fieldMap "Age" |> ignore)
    Assert.That(ex.Message, Is.EqualTo("Missing field \"Age\" for App.Person"))
    Assert.That(DecodeWire.field "App.Person" fieldMap "Name", Is.EqualTo(JsonValue.String "x"))

[<Test>]
let ``A union case with the wrong number of fields is rejected`` () =
    let one = [ JsonValue.Number 1m ]
    Assert.That(DecodeWire.caseFields "App.Shape" "Circle" 1 one, Is.EqualTo([| JsonValue.Number 1m |]))
    let ex = Assert.Catch(fun () -> DecodeWire.caseFields "App.Shape" "Rect" 2 one |> ignore)
    Assert.That(ex.Message, Is.EqualTo("Union case App.Shape.Rect expects 2 field(s), got 1"))
    Assert.Catch(fun () -> DecodeWire.caseFields "App.Shape" "Point" 0 one |> ignore) |> ignore

[<Test>]
let ``A tuple with the wrong number of elements is rejected`` () =
    let ex = Assert.Catch(fun () -> DecodeWire.tupleItems "(int * string)" 2 [ JsonValue.Number 1m ] |> ignore)
    Assert.That(ex.Message, Is.EqualTo("Expected 2 elements for (int * string), got 1"))
    Assert.Catch(fun () -> DecodeWire.tupleItems "(int * string)" 2 [ JsonValue.Number 1m; JsonValue.String "a"; JsonValue.Null ] |> ignore) |> ignore

[<Test>]
let ``Unknown cases and wrong JSON kinds name the type`` () =
    let ex = Assert.Catch(fun () -> DecodeWire.unknownCase<int> "App.Shape" "Hexagon" |> ignore)
    Assert.That(ex.Message, Is.EqualTo("Unknown case \"Hexagon\" for App.Shape"))
    let ex = Assert.Catch(fun () -> DecodeWire.expected<int> "a JSON object" "App.Person" (JsonValue.Array []) |> ignore)
    Assert.That(ex.Message, Is.EqualTo("Expected a JSON object for App.Person, got an array"))
