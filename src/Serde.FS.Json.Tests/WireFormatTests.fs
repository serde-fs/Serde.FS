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
            Assert.That(SerdeJson.serialize 1234.5m, Is.EqualTo("1234.5"), culture)
            Assert.That(SerdeJson.serialize -5, Is.EqualTo("-5"), culture)
            Assert.That(SerdeJson.serialize -9000000000L, Is.EqualTo("-9000000000"), culture)
            Assert.That(roundTrip 1.5, Is.EqualTo(1.5), culture)
            Assert.That(roundTrip 1234.5m, Is.EqualTo(1234.5m), culture)
            Assert.That(roundTrip -5, Is.EqualTo(-5), culture))

[<Test>]
let ``Dates and times round-trip in every culture`` () =
    let dateTime = DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc)
    let dateTimeOffset = DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours 2.)
    let dateOnly = DateOnly(2026, 1, 2)
    let timeOnly = TimeOnly(3, 4, 5, 678)
    for culture in cultures do
        withCulture culture (fun () ->
            Assert.That(SerdeJson.serialize dateTime, Is.EqualTo("\"2026-01-02T03:04:05.6780000Z\""), culture)
            Assert.That(SerdeJson.serialize dateTimeOffset, Is.EqualTo("\"2026-01-02T03:04:05.0000000+02:00\""), culture)
            Assert.That(SerdeJson.serialize dateOnly, Is.EqualTo("\"2026-01-02\""), culture)
            Assert.That(SerdeJson.serialize timeOnly, Is.EqualTo("\"03:04:05.6780000\""), culture)
            Assert.That(roundTrip dateTime, Is.EqualTo(dateTime), culture)
            Assert.That(roundTrip dateTimeOffset, Is.EqualTo(dateTimeOffset), culture)
            Assert.That(roundTrip dateOnly, Is.EqualTo(dateOnly), culture)
            Assert.That(roundTrip timeOnly, Is.EqualTo(timeOnly), culture))

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

// -- Other numeric types are unaffected by the float representation --

[<Test>]
let ``int int64 and decimal keep their wire shape`` () =
    Assert.That(SerdeJson.serialize 42, Is.EqualTo("42"))
    Assert.That(SerdeJson.serialize Int32.MinValue, Is.EqualTo("-2147483648"))
    Assert.That(SerdeJson.serialize 1.50m, Is.EqualTo("1.50"))
    Assert.That(roundTrip Int32.MaxValue, Is.EqualTo(Int32.MaxValue))
    Assert.That(roundTrip Int64.MaxValue, Is.EqualTo(Int64.MaxValue))
    Assert.That(roundTrip Int64.MinValue, Is.EqualTo(Int64.MinValue))
    Assert.That(roundTrip Decimal.MaxValue, Is.EqualTo(Decimal.MaxValue))
    Assert.That(roundTrip 0.0000000000000000000000000001m, Is.EqualTo(0.0000000000000000000000000001m))

[<Test>]
let ``Integers and decimals still decode exponent notation`` () =
    Assert.That(SerdeJson.deserialize<int> "1e2", Is.EqualTo(100))
    Assert.That(SerdeJson.deserialize<int64> "1E3", Is.EqualTo(1000L))
    Assert.That(SerdeJson.deserialize<decimal> "1.5e1", Is.EqualTo(15m))
