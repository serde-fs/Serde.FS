module Serde.FS.SourceGen.Tests.AstParserTests

open NUnit.Framework
open Serde.FS.SourceGen

[<Test>]
let ``Parses record with Serde attribute`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Person = { FName: string; LName: string; Age: int }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Namespace, Is.EqualTo("MyApp"))
    Assert.That(t.TypeName, Is.EqualTo("Person"))
    Assert.That(t.Capability, Is.EqualTo(Both))
    Assert.That(t.Fields.Length, Is.EqualTo(3))

    Assert.That(t.Fields.[0].Name, Is.EqualTo("FName"))
    Assert.That(t.Fields.[0].FSharpType, Is.EqualTo("string"))

    Assert.That(t.Fields.[1].Name, Is.EqualTo("LName"))
    Assert.That(t.Fields.[1].FSharpType, Is.EqualTo("string"))

    Assert.That(t.Fields.[2].Name, Is.EqualTo("Age"))
    Assert.That(t.Fields.[2].FSharpType, Is.EqualTo("int"))

[<Test>]
let ``Parses record with SerdeSerialize attribute`` () =
    let source = """
namespace MyApp

[<Serde.FS.SerdeSerialize>]
type Point = { X: float; Y: float }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.TypeName, Is.EqualTo("Point"))
    Assert.That(t.Capability, Is.EqualTo(Serialize))

[<Test>]
let ``Parses record with SerdeDeserialize attribute`` () =
    let source = """
namespace MyApp

[<SerdeDeserialize>]
type Config = { Host: string; Port: int }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.TypeName, Is.EqualTo("Config"))
    Assert.That(t.Capability, Is.EqualTo(Deserialize))

[<Test>]
let ``Ignores types without Serde attributes`` () =
    let source = """
namespace MyApp

type NotSerde = { X: int }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(0))

[<Test>]
let ``Parses multiple annotated records in one file`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Person = { Name: string; Age: int }

[<Serde>]
type Address = { Street: string; City: string; Zip: string }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(2))
    Assert.That(types.[0].TypeName, Is.EqualTo("Person"))
    Assert.That(types.[1].TypeName, Is.EqualTo("Address"))

[<Test>]
let ``Parses record with option field`` () =
    let source = """
namespace MyApp

[<Serde>]
type Person = { Name: string; MiddleName: string option }
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Fields.[1].Name, Is.EqualTo("MiddleName"))
    Assert.That(t.Fields.[1].FSharpType, Is.EqualTo("string option"))

[<Test>]
let ``Parses record with various supported types`` () =
    let source = """
namespace MyApp

[<Serde>]
type AllTypes = {
    S: string
    I: int
    I64: int64
    F: float
    D: decimal
    B: bool
    Dt: System.DateTime
    G: System.Guid
}
"""
    let types = AstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Fields.Length, Is.EqualTo(8))
    Assert.That(t.Fields.[0].FSharpType, Is.EqualTo("string"))
    Assert.That(t.Fields.[1].FSharpType, Is.EqualTo("int"))
    Assert.That(t.Fields.[2].FSharpType, Is.EqualTo("int64"))
    Assert.That(t.Fields.[3].FSharpType, Is.EqualTo("float"))
    Assert.That(t.Fields.[4].FSharpType, Is.EqualTo("decimal"))
    Assert.That(t.Fields.[5].FSharpType, Is.EqualTo("bool"))
    Assert.That(t.Fields.[6].FSharpType, Is.EqualTo("System.DateTime"))
    Assert.That(t.Fields.[7].FSharpType, Is.EqualTo("System.Guid"))
