module Serde.FS.Json.Tests.SerdeTests

open NUnit.Framework
open Serde.FS

type Person = { FName: string; LName: string }

[<Serde>]
type MarkedPerson = { FName: string; LName: string }

[<Serde>]
type TestWrapper<'T> = TestWrapper of 'T

[<OneTimeSetUp>]
let OneTimeSetup () =
    // Register a concrete TestWrapper<MarkedPerson> so the wrapper lookup can find it.
    SerdeMetadata.register typeof<TestWrapper<MarkedPerson>>

[<SetUp>]
let Setup () =
    Serde.FS.Json.SerdeJson.setAsDefaultBackend()

[<Test>]
let ``Throws on serialize for type without generated metadata`` () =
    let mutable threw = false
    try
        Serde.Serialize<Person>({ FName = "Jordan"; LName = "Marr" }) |> ignore
    with _ ->
        threw <- true
    Assert.That(threw, Is.True, "Expected strict mode to throw for Person type without generated metadata")

[<Test>]
let ``Throws on deserialize for type without generated metadata`` () =
    let mutable threw = false
    try
        Serde.Deserialize<Person>("""{"FName":"Jordan","LName":"Marr"}""") |> ignore
    with _ ->
        threw <- true
    Assert.That(threw, Is.True, "Expected strict mode to throw for Person type without generated metadata")

[<Test>]
let ``Throws specialized error when deserializing generic wrapper without type argument`` () =
    let json = """{"TestWrapper":{"FName":"Jordan","LName":"Marr"}}"""
    let ex = Assert.Throws<SerdeMissingMetadataException>(fun () ->
        Serde.Deserialize<obj>(json) |> ignore
    )
    Assert.That(ex.Message, Does.Contain("Cannot deserialize a generic wrapper type"))
    Assert.That(ex.Message, Does.Contain("TestWrapper<_>"))

[<Test>]
let ``Non-wrapper JSON still throws normal missing metadata error`` () =
    let json = """{"FName":"Jordan","LName":"Marr"}"""
    let ex = Assert.Throws<SerdeMissingMetadataException>(fun () ->
        Serde.Deserialize<Person>(json) |> ignore
    )
    Assert.That(ex.Message, Does.Contain("Missing metadata for type"))
