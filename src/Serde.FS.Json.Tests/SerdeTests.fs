module Serde.FS.Json.Tests.SerdeTests

open NUnit.Framework
open Serde.FS
open System.Text.Json
open System.Text.Json.Serialization.Metadata

type Person = { FName: string; LName: string }

[<Serde>]
type MarkedPerson = { FName: string; LName: string }

[<Serde>]
type TestWrapper<'T> = TestWrapper of 'T

/// Test resolver that provides metadata for MarkedPerson.
type TestResolver() =
    interface IJsonTypeInfoResolver with
        member _.GetTypeInfo(ty, options) =
            if ty = typeof<MarkedPerson> then
                JsonTypeInfo.CreateJsonTypeInfo(typeof<MarkedPerson>, options)
            else
                null

[<OneTimeSetUp>]
let OneTimeSetup () =
    // Register only in generatedResolvers (for strict mode checking),
    // not in TypeInfoResolverChain (which would need complete metadata for STJ serialization).
    Serde.FS.Json.JsonOptionsCache.generatedResolvers.Add(TestResolver())
    // Register a concrete TestWrapper<MarkedPerson> so the wrapper lookup can find it.
    SerdeMetadata.register typeof<TestWrapper<MarkedPerson>>

[<SetUp>]
let Setup () =
    Serde.FS.Json.SerdeJson.useAsDefault()

[<Test>]
let ``Serialize and deserialize a record with generated metadata`` () =
    let json = Serde.Serialize { MarkedPerson.FName = "Jordan"; LName = "Marr" }
    json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

    let person : MarkedPerson = Serde.Deserialize json
    person.FName =! "Jordan"
    person.LName =! "Marr"

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
let ``Succeeds for type with registered resolver`` () =
    let json = Serde.Serialize { MarkedPerson.FName = "Jordan"; LName = "Marr" }
    json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

    let person : MarkedPerson = Serde.Deserialize json
    person.FName =! "Jordan"
    person.LName =! "Marr"

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
