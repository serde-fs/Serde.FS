module Serde.FS.Json.Tests.SerdeTests

open NUnit.Framework
open Serde.FS
open System.Text.Json
open System.Text.Json.Serialization.Metadata

type Person = { FName: string; LName: string }

[<Serde>]
type MarkedPerson = { FName: string; LName: string }

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

[<SetUp>]
let Setup () =
    Serde.FS.Json.SerdeJson.useAsDefault()
    // Existing tests use reflection (no source gen), so allow it.
    Serde.FS.Json.SerdeJson.allowReflectionFallback()

[<Test>]
let ``Serialize and deserialize a record`` () =
    let json = Serde.Serialize { FName = "Jordan"; LName = "Marr" }
    json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

    let person : Person = Serde.Deserialize json
    person.FName =! "Jordan"
    person.LName =! "Marr"

[<Test>]
let ``Strict mode throws on serialize for type without generated metadata`` () =
    Serde.Strict <- true
    try
        let mutable threw = false
        try
            Serde.Serialize<Person>({ FName = "Jordan"; LName = "Marr" }) |> ignore
        with _ ->
            threw <- true
        Assert.That(threw, Is.True, "Expected strict mode to throw for Person type without generated metadata")
    finally
        Serde.Strict <- false

[<Test>]
let ``Strict mode throws on deserialize for type without generated metadata`` () =
    Serde.Strict <- true
    try
        let mutable threw = false
        try
            Serde.Deserialize<Person>("""{"FName":"Jordan","LName":"Marr"}""") |> ignore
        with _ ->
            threw <- true
        Assert.That(threw, Is.True, "Expected strict mode to throw for Person type without generated metadata")
    finally
        Serde.Strict <- false

[<Test>]
let ``Strict mode succeeds after allowReflectionFallback`` () =
    Serde.Strict <- true
    Serde.FS.Json.SerdeJson.allowReflectionFallback()
    let json = Serde.Serialize { FName = "Jordan"; LName = "Marr" }
    json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

[<Test>]
let ``Strict mode does not throw for type with registered resolver`` () =
    Serde.Strict <- true
    try
        let json = Serde.Serialize { MarkedPerson.FName = "Jordan"; LName = "Marr" }
        json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

        let person : MarkedPerson = Serde.Deserialize json
        person.FName =! "Jordan"
        person.LName =! "Marr"
    finally
        Serde.Strict <- false
