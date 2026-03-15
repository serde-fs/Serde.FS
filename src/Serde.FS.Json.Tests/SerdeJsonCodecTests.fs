module Serde.FS.Json.Tests.SerdeJsonCodecTests

open System
open NUnit.Framework
open Serde.FS
open Serde.FS.Json
open Serde.FS.Json.Codec

// ---------------------------------------------------------------------------
// Primitive round-trip tests
// ---------------------------------------------------------------------------

[<Test>]
let ``serialize bool produces correct JSON`` () =
    Assert.AreEqual("true", SerdeJson.serialize true)
    Assert.AreEqual("false", SerdeJson.serialize false)

[<Test>]
let ``deserialize bool produces correct value`` () =
    Assert.AreEqual(true, SerdeJson.deserialize<bool> "true")
    Assert.AreEqual(false, SerdeJson.deserialize<bool> "false")

[<Test>]
let ``serialize int round-trips`` () =
    let json = SerdeJson.serialize 42
    Assert.AreEqual("42", json)
    Assert.AreEqual(42, SerdeJson.deserialize<int> json)

[<Test>]
let ``serialize int64 round-trips`` () =
    let json = SerdeJson.serialize 9999999999L
    Assert.AreEqual("9999999999", json)
    Assert.AreEqual(9999999999L, SerdeJson.deserialize<int64> json)

[<Test>]
let ``serialize float round-trips`` () =
    let json = SerdeJson.serialize 3.14
    let result = SerdeJson.deserialize<float> json
    Assert.AreEqual(3.14, result, 0.001)

[<Test>]
let ``serialize string round-trips`` () =
    let json = SerdeJson.serialize "hello world"
    Assert.AreEqual("\"hello world\"", json)
    Assert.AreEqual("hello world", SerdeJson.deserialize<string> json)

[<Test>]
let ``serialize decimal round-trips`` () =
    let json = SerdeJson.serialize 123.456m
    Assert.AreEqual(123.456m, SerdeJson.deserialize<decimal> json)

[<Test>]
let ``serialize Guid round-trips`` () =
    let guid = Guid.Parse("12345678-1234-1234-1234-123456789012")
    let json = SerdeJson.serialize guid
    Assert.AreEqual("\"12345678-1234-1234-1234-123456789012\"", json)
    Assert.AreEqual(guid, SerdeJson.deserialize<Guid> json)

[<Test>]
let ``serialize DateTime round-trips`` () =
    let dt = DateTime(2026, 3, 14, 10, 30, 0, DateTimeKind.Utc)
    let json = SerdeJson.serialize dt
    let result = SerdeJson.deserialize<DateTime> json
    Assert.AreEqual(dt, result)

[<Test>]
let ``serialize DateOnly round-trips`` () =
    let d = DateOnly(2026, 3, 14)
    let json = SerdeJson.serialize d
    Assert.AreEqual("\"2026-03-14\"", json)
    Assert.AreEqual(d, SerdeJson.deserialize<DateOnly> json)

[<Test>]
let ``serialize TimeOnly round-trips`` () =
    let t = TimeOnly(10, 30, 0)
    let json = SerdeJson.serialize t
    Assert.AreEqual(t, SerdeJson.deserialize<TimeOnly> json)

[<Test>]
let ``serialize byte array round-trips`` () =
    let bytes = [| 1uy; 2uy; 3uy; 4uy |]
    let json = SerdeJson.serialize bytes
    let result = SerdeJson.deserialize<byte[]> json
    Assert.AreEqual(bytes, result)

[<Test>]
let ``serialize unit round-trips`` () =
    let json = SerdeJson.serialize ()
    Assert.AreEqual("null", json)
    Assert.AreEqual((), SerdeJson.deserialize<unit> json)

// ---------------------------------------------------------------------------
// UTF-8 byte array variants
// ---------------------------------------------------------------------------

[<Test>]
let ``serializeToUtf8 produces valid UTF-8 bytes`` () =
    let bytes = SerdeJson.serializeToUtf8 42
    let json = System.Text.Encoding.UTF8.GetString(bytes)
    Assert.AreEqual("42", json)

[<Test>]
let ``deserializeFromUtf8 parses valid UTF-8 bytes`` () =
    let bytes = System.Text.Encoding.UTF8.GetBytes("\"hello\"")
    let result = SerdeJson.deserializeFromUtf8<string> bytes
    Assert.AreEqual("hello", result)

[<Test>]
let ``serializeToUtf8 and deserializeFromUtf8 round-trip`` () =
    let original = "round trip test"
    let bytes = SerdeJson.serializeToUtf8 original
    let result = SerdeJson.deserializeFromUtf8<string> bytes
    Assert.AreEqual(original, result)

// ---------------------------------------------------------------------------
// Error handling tests
// ---------------------------------------------------------------------------

[<Test>]
let ``deserialize invalid JSON throws SerdeJsonParseException`` () =
    let ex =
        Assert.Throws<SerdeJsonParseException>(fun () ->
            SerdeJson.deserialize<int> "not valid json" |> ignore
        )
    Assert.That(ex.Position, Is.GreaterThanOrEqualTo(0))
    // Verify it is also catchable as SerdeJsonException
    Assert.That(ex, Is.InstanceOf<SerdeJsonException>())

[<Test>]
let ``serialize unregistered type throws SerdeCodecNotFoundException`` () =
    Assert.Throws<SerdeCodecNotFoundException>(fun () ->
        SerdeJson.serialize {| X = 1 |} |> ignore
    ) |> ignore

// ---------------------------------------------------------------------------
// Custom codec via registry
// ---------------------------------------------------------------------------

type UpperString = { Value: string }

type UpperStringCodec() =
    interface IJsonCodec<UpperString> with
        member _.Encode v = JsonValue.String(v.Value.ToUpperInvariant())
        member _.Decode json =
            match json with
            | JsonValue.String s -> { Value = s }
            | _ -> failwith "Expected string"

    interface IJsonCodec with
        member _.Type = typeof<UpperString>
        member _.Encode obj = JsonValue.String((obj :?> UpperString).Value.ToUpperInvariant())
        member _.Decode json =
            match json with
            | JsonValue.String s -> box { Value = s }
            | _ -> failwith "Expected string"

[<Test>]
let ``serialize with custom registered codec`` () =
    let original = GlobalCodecRegistry.Current
    try
        GlobalCodecRegistry.Current <-
            GlobalCodecRegistry.Current
            |> CodecRegistry.add (typeof<UpperString>, UpperStringCodec() :> IJsonCodec)
        let json = SerdeJson.serialize { Value = "hello" }
        Assert.AreEqual("\"HELLO\"", json)
        let result = SerdeJson.deserialize<UpperString> json
        Assert.AreEqual("HELLO", result.Value)
    finally
        GlobalCodecRegistry.Current <- original
