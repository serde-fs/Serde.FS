module Serde.FS.Json.Tests.CodecTests

open System
open NUnit.Framework
open Serde.FS.Json.Codec

// -- JsonValue construction tests --

[<Test>]
let ``JsonValue Null constructs correctly`` () =
    let v = Null
    Assert.AreEqual("null", JsonValue.toString v)

[<Test>]
let ``JsonValue Bool constructs correctly`` () =
    Assert.AreEqual("true", JsonValue.toString (Bool true))
    Assert.AreEqual("false", JsonValue.toString (Bool false))

[<Test>]
let ``JsonValue Number constructs correctly`` () =
    Assert.AreEqual("42", JsonValue.toString (Number 42m))

[<Test>]
let ``JsonValue String constructs correctly`` () =
    Assert.AreEqual("\"hello\"", JsonValue.toString (String "hello"))

[<Test>]
let ``JsonValue Array constructs correctly`` () =
    let v = Array [ Number 1m; String "two"; Bool true ]
    Assert.AreEqual("[1, \"two\", true]", JsonValue.toString v)

[<Test>]
let ``JsonValue Object constructs correctly`` () =
    let v = Object [ "name", String "Alice"; "age", Number 30m ]
    Assert.AreEqual("{\"name\": \"Alice\", \"age\": 30}", JsonValue.toString v)

// -- CodecRegistry tests --

type DummyEncoder() =
    interface IJsonEncoder<int> with
        member _.Encode(x) = Number (decimal x)

type DummyDecoder() =
    interface IJsonDecoder<int> with
        member _.Decode(v) =
            match v with
            | Number n -> int n
            | _ -> failwith "Expected Number"

[<Test>]
let ``CodecRegistry can add and retrieve encoder`` () =
    let registry = CodecRegistry()
    registry.Add(DummyEncoder(), DummyDecoder())

    let enc = registry.TryGetEncoder<int>()
    Assert.IsTrue(enc.IsSome)
    Assert.AreEqual(Number 42m, enc.Value.Encode(42))

[<Test>]
let ``CodecRegistry can add and retrieve decoder`` () =
    let registry = CodecRegistry()
    registry.Add(DummyEncoder(), DummyDecoder())

    let dec = registry.TryGetDecoder<int>()
    Assert.IsTrue(dec.IsSome)
    Assert.AreEqual(42, dec.Value.Decode(Number 42m))

[<Test>]
let ``CodecRegistry returns None for missing types`` () =
    let registry = CodecRegistry()

    let enc = registry.TryGetEncoder<string>()
    let dec = registry.TryGetDecoder<string>()
    Assert.IsTrue(enc.IsNone)
    Assert.IsTrue(dec.IsNone)

// -- Primitive codec encoding tests --

[<Test>]
let ``PrimitiveCodecs bool encodes correctly`` () =
    Assert.AreEqual(Bool true, PrimitiveCodecs.boolEncoder.Encode true)
    Assert.AreEqual(Bool false, PrimitiveCodecs.boolEncoder.Encode false)

[<Test>]
let ``PrimitiveCodecs string encodes correctly`` () =
    Assert.AreEqual(String "abc", PrimitiveCodecs.stringEncoder.Encode "abc")

[<Test>]
let ``PrimitiveCodecs int encodes correctly`` () =
    Assert.AreEqual(Number 42m, PrimitiveCodecs.intEncoder.Encode 42)

[<Test>]
let ``PrimitiveCodecs float encodes correctly`` () =
    Assert.AreEqual(Number 1.5m, PrimitiveCodecs.floatEncoder.Encode 1.5)

[<Test>]
let ``PrimitiveCodecs decimal encodes correctly`` () =
    Assert.AreEqual(Number 3.14m, PrimitiveCodecs.decimalEncoder.Encode 3.14m)

[<Test>]
let ``PrimitiveCodecs unit encodes correctly`` () =
    Assert.AreEqual(Null, PrimitiveCodecs.unitEncoder.Encode ())

[<Test>]
let ``PrimitiveCodecs byte array encodes as Base64`` () =
    let bytes = [| 1uy; 2uy; 3uy |]
    let expected = String (Convert.ToBase64String bytes)
    Assert.AreEqual(expected, PrimitiveCodecs.byteArrayEncoder.Encode bytes)

// -- Primitive codec round-trip tests --

[<Test>]
let ``PrimitiveCodecs bool round-trips`` () =
    let v = true
    Assert.AreEqual(v, PrimitiveCodecs.boolDecoder.Decode(PrimitiveCodecs.boolEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs string round-trips`` () =
    let v = "hello"
    Assert.AreEqual(v, PrimitiveCodecs.stringDecoder.Decode(PrimitiveCodecs.stringEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs int round-trips`` () =
    let v = 99
    Assert.AreEqual(v, PrimitiveCodecs.intDecoder.Decode(PrimitiveCodecs.intEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs float round-trips`` () =
    let v = 2.718
    Assert.AreEqual(v, PrimitiveCodecs.floatDecoder.Decode(PrimitiveCodecs.floatEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs decimal round-trips`` () =
    let v = 123.456m
    Assert.AreEqual(v, PrimitiveCodecs.decimalDecoder.Decode(PrimitiveCodecs.decimalEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs unit round-trips`` () =
    let v = ()
    Assert.AreEqual(v, PrimitiveCodecs.unitDecoder.Decode(PrimitiveCodecs.unitEncoder.Encode v))

[<Test>]
let ``PrimitiveCodecs byte array round-trips`` () =
    let v = [| 1uy; 2uy; 3uy |]
    Assert.AreEqual(v, PrimitiveCodecs.byteArrayDecoder.Decode(PrimitiveCodecs.byteArrayEncoder.Encode v))

// -- CodecRegistry.WithPrimitives tests --

[<Test>]
let ``WithPrimitives registers all primitive encoders`` () =
    let registry = CodecRegistry.WithPrimitives()
    Assert.IsTrue(registry.TryGetEncoder<bool>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<string>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<decimal>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<int>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<float>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<unit>().IsSome)
    Assert.IsTrue(registry.TryGetEncoder<byte[]>().IsSome)

[<Test>]
let ``WithPrimitives registers all primitive decoders`` () =
    let registry = CodecRegistry.WithPrimitives()
    Assert.IsTrue(registry.TryGetDecoder<bool>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<string>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<decimal>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<int>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<float>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<unit>().IsSome)
    Assert.IsTrue(registry.TryGetDecoder<byte[]>().IsSome)

[<Test>]
let ``WithPrimitives returns None for unregistered types`` () =
    let registry = CodecRegistry.WithPrimitives()
    Assert.IsTrue(registry.TryGetEncoder<DateTime>().IsNone)
    Assert.IsTrue(registry.TryGetDecoder<DateTime>().IsNone)
