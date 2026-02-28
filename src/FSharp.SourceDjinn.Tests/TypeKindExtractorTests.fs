module FSharp.SourceDjinn.Tests.TypeKindExtractorTests

open NUnit.Framework
open Serde.FS.TypeKindTypes
open FSharp.SourceDjinn

[<Test>]
let ``Extracts all primitive types from record fields`` () =
    let source = """
namespace TestNs

type Primitives = {
    A: unit
    B: bool
    C: sbyte
    D: int16
    E: int
    F: int64
    G: byte
    H: uint16
    I: uint32
    J: uint64
    K: float32
    L: float
    M: decimal
    N: string
    O: System.Guid
    P: System.DateTime
    Q: System.DateTimeOffset
    R: System.TimeSpan
    S: System.DateOnly
    T: System.TimeOnly
}
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.TypeName, Is.EqualTo("Primitives"))
    match t.Kind with
    | Record fields ->
        Assert.That(fields.Length, Is.EqualTo(20))
        Assert.That(fields.[0].Type.Kind, Is.EqualTo(Primitive Unit))
        Assert.That(fields.[1].Type.Kind, Is.EqualTo(Primitive Bool))
        Assert.That(fields.[2].Type.Kind, Is.EqualTo(Primitive Int8))
        Assert.That(fields.[3].Type.Kind, Is.EqualTo(Primitive Int16))
        Assert.That(fields.[4].Type.Kind, Is.EqualTo(Primitive Int32))
        Assert.That(fields.[5].Type.Kind, Is.EqualTo(Primitive Int64))
        Assert.That(fields.[6].Type.Kind, Is.EqualTo(Primitive UInt8))
        Assert.That(fields.[7].Type.Kind, Is.EqualTo(Primitive UInt16))
        Assert.That(fields.[8].Type.Kind, Is.EqualTo(Primitive UInt32))
        Assert.That(fields.[9].Type.Kind, Is.EqualTo(Primitive UInt64))
        Assert.That(fields.[10].Type.Kind, Is.EqualTo(Primitive Float32))
        Assert.That(fields.[11].Type.Kind, Is.EqualTo(Primitive Float64))
        Assert.That(fields.[12].Type.Kind, Is.EqualTo(Primitive Decimal))
        Assert.That(fields.[13].Type.Kind, Is.EqualTo(Primitive String))
        Assert.That(fields.[14].Type.Kind, Is.EqualTo(Primitive Guid))
        Assert.That(fields.[15].Type.Kind, Is.EqualTo(Primitive DateTime))
        Assert.That(fields.[16].Type.Kind, Is.EqualTo(Primitive DateTimeOffset))
        Assert.That(fields.[17].Type.Kind, Is.EqualTo(Primitive TimeSpan))
        Assert.That(fields.[18].Type.Kind, Is.EqualTo(Primitive DateOnly))
        Assert.That(fields.[19].Type.Kind, Is.EqualTo(Primitive TimeOnly))
    | _ -> Assert.Fail("Expected Record kind")

[<Test>]
let ``Extracts simple record with namespace and fields`` () =
    let source = """
namespace MyApp

type Person = { FName: string; LName: string; Age: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Namespace, Is.EqualTo(Some "MyApp"))
    Assert.That(t.TypeName, Is.EqualTo("Person"))
    match t.Kind with
    | Record fields ->
        Assert.That(fields.Length, Is.EqualTo(3))
        Assert.That(fields.[0].Name, Is.EqualTo("FName"))
        Assert.That(fields.[0].Type.Kind, Is.EqualTo(Primitive String))
        Assert.That(fields.[1].Name, Is.EqualTo("LName"))
        Assert.That(fields.[1].Type.Kind, Is.EqualTo(Primitive String))
        Assert.That(fields.[2].Name, Is.EqualTo("Age"))
        Assert.That(fields.[2].Type.Kind, Is.EqualTo(Primitive Int32))
    | _ -> Assert.Fail("Expected Record kind")

[<Test>]
let ``Extracts tuple as field type`` () =
    let source = """
namespace TestNs

type WithTuple = { Pair: int * string }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        Assert.That(fields.[0].Name, Is.EqualTo("Pair"))
        match fields.[0].Type.Kind with
        | Tuple elements ->
            Assert.That(elements.Length, Is.EqualTo(2))
            Assert.That(elements.[0].Name, Is.EqualTo("Item1"))
            Assert.That(elements.[0].Type.Kind, Is.EqualTo(Primitive Int32))
            Assert.That(elements.[1].Name, Is.EqualTo("Item2"))
            Assert.That(elements.[1].Type.Kind, Is.EqualTo(Primitive String))
        | other -> Assert.Fail(sprintf "Expected Tuple kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts option as field type`` () =
    let source = """
namespace TestNs

type WithOption = { Name: string option }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | Option inner ->
            Assert.That(inner.Kind, Is.EqualTo(Primitive String))
        | other -> Assert.Fail(sprintf "Expected Option kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts list as field type`` () =
    let source = """
namespace TestNs

type WithList = { Items: string list }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | List inner ->
            Assert.That(inner.Kind, Is.EqualTo(Primitive String))
        | other -> Assert.Fail(sprintf "Expected List kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts array as field type`` () =
    let source = """
namespace TestNs

type WithArray = { Items: int array }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | Array inner ->
            Assert.That(inner.Kind, Is.EqualTo(Primitive Int32))
        | other -> Assert.Fail(sprintf "Expected Array kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts set as field type`` () =
    let source = """
namespace TestNs

type WithSet = { Tags: Set<string> }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | Set inner ->
            Assert.That(inner.Kind, Is.EqualTo(Primitive String))
        | other -> Assert.Fail(sprintf "Expected Set kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts map as field type`` () =
    let source = """
namespace TestNs

type WithMap = { Lookup: Map<string, int> }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | Map(key, value) ->
            Assert.That(key.Kind, Is.EqualTo(Primitive String))
            Assert.That(value.Kind, Is.EqualTo(Primitive Int32))
        | other -> Assert.Fail(sprintf "Expected Map kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts enum type`` () =
    let source = """
namespace TestNs

type Color =
    | Red = 0
    | Green = 1
    | Blue = 2
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.TypeName, Is.EqualTo("Color"))
    match t.Kind with
    | Enum cases ->
        Assert.That(cases.Length, Is.EqualTo(3))
        Assert.That(cases.[0].CaseName, Is.EqualTo("Red"))
        Assert.That(cases.[0].Value, Is.EqualTo(0))
        Assert.That(cases.[1].CaseName, Is.EqualTo("Green"))
        Assert.That(cases.[1].Value, Is.EqualTo(1))
        Assert.That(cases.[2].CaseName, Is.EqualTo("Blue"))
        Assert.That(cases.[2].Value, Is.EqualTo(2))
    | other -> Assert.Fail(sprintf "Expected Enum kind, got %A" other)

[<Test>]
let ``Extracts anonymous record as field type`` () =
    let source = """
namespace TestNs

type WithAnon = { Data: {| Name: string; Age: int |} }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        match fields.[0].Type.Kind with
        | AnonymousRecord anonFields ->
            Assert.That(anonFields.Length, Is.EqualTo(2))
            // Anonymous record fields are sorted alphabetically by FCS
            let nameField = anonFields |> List.find (fun f -> f.Name = "Name")
            let ageField = anonFields |> List.find (fun f -> f.Name = "Age")
            Assert.That(nameField.Type.Kind, Is.EqualTo(Primitive String))
            Assert.That(ageField.Type.Kind, Is.EqualTo(Primitive Int32))
        | other -> Assert.Fail(sprintf "Expected AnonymousRecord kind, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Record kind, got %A" other)

[<Test>]
let ``Extracts simple union with cases`` () =
    let source = """
namespace TestNs

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Point
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.TypeName, Is.EqualTo("Shape"))
    match t.Kind with
    | Union cases ->
        Assert.That(cases.Length, Is.EqualTo(3))

        Assert.That(cases.[0].CaseName, Is.EqualTo("Circle"))
        Assert.That(cases.[0].Fields.Length, Is.EqualTo(1))
        Assert.That(cases.[0].Fields.[0].Name, Is.EqualTo("radius"))
        Assert.That(cases.[0].Fields.[0].Type.Kind, Is.EqualTo(Primitive Float64))
        Assert.That(cases.[0].Tag, Is.EqualTo(Some 0))

        Assert.That(cases.[1].CaseName, Is.EqualTo("Rectangle"))
        Assert.That(cases.[1].Fields.Length, Is.EqualTo(2))
        Assert.That(cases.[1].Fields.[0].Name, Is.EqualTo("width"))
        Assert.That(cases.[1].Fields.[0].Type.Kind, Is.EqualTo(Primitive Float64))
        Assert.That(cases.[1].Fields.[1].Name, Is.EqualTo("height"))
        Assert.That(cases.[1].Fields.[1].Type.Kind, Is.EqualTo(Primitive Float64))
        Assert.That(cases.[1].Tag, Is.EqualTo(Some 1))

        Assert.That(cases.[2].CaseName, Is.EqualTo("Point"))
        Assert.That(cases.[2].Fields.Length, Is.EqualTo(0))
        Assert.That(cases.[2].Tag, Is.EqualTo(Some 2))
    | other -> Assert.Fail(sprintf "Expected Union kind, got %A" other)

// --- Attribute extraction tests (Spec 10 §4) ---

[<Test>]
let ``Extracts type-level attribute with constructor arg`` () =
    let source = """
namespace TestNs

[<Serde.FS.SerdeRename("Foo")>]
type X = { A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Attributes.Length, Is.EqualTo(1))
    Assert.That(t.Attributes.[0].Name, Is.EqualTo("Serde.FS.SerdeRenameAttribute"))
    Assert.That(t.Attributes.[0].ConstructorArgs, Is.EqualTo([box "Foo"]))
    Assert.That(t.Attributes.[0].NamedArgs, Is.Empty)

[<Test>]
let ``Extracts field-level attribute`` () =
    let source = """
namespace TestNs

type X = { [<SerdeSkip>] A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Record fields ->
        Assert.That(fields.[0].Attributes.Length, Is.EqualTo(1))
        Assert.That(fields.[0].Attributes.[0].Name, Is.EqualTo("SerdeSkipAttribute"))
        Assert.That(fields.[0].Attributes.[0].ConstructorArgs, Is.Empty)
    | _ -> Assert.Fail("Expected Record kind")

[<Test>]
let ``Extracts union case attribute with constructor arg`` () =
    let source = """
namespace TestNs

type U =
    | [<Serde.FS.SerdeRename("Bar")>] C of int
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Union cases ->
        Assert.That(cases.[0].Attributes.Length, Is.EqualTo(1))
        Assert.That(cases.[0].Attributes.[0].Name, Is.EqualTo("Serde.FS.SerdeRenameAttribute"))
        Assert.That(cases.[0].Attributes.[0].ConstructorArgs, Is.EqualTo([box "Bar"]))
    | _ -> Assert.Fail("Expected Union kind")

[<Test>]
let ``Extracts non-Serde attribute`` () =
    let source = """
namespace TestNs

open System

[<Obsolete("x")>]
type X = { A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Attributes.Length, Is.EqualTo(1))
    Assert.That(t.Attributes.[0].Name, Is.EqualTo("ObsoleteAttribute"))
    Assert.That(t.Attributes.[0].ConstructorArgs, Is.EqualTo([box "x"]))

[<Test>]
let ``Extracts named arguments from attribute`` () =
    let source = """
namespace TestNs

open System

[<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
type X = { A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Attributes.Length, Is.EqualTo(1))
    Assert.That(t.Attributes.[0].Name, Is.EqualTo("AttributeUsageAttribute"))
    // First arg is positional (AttributeTargets.All is not a simple const, so may not extract)
    // Named arg AllowMultiple = false should be extracted
    Assert.That(t.Attributes.[0].NamedArgs, Does.Contain(("AllowMultiple", box false)))

[<Test>]
let ``Extracts multiple attributes on a type`` () =
    let source = """
namespace TestNs

open System

[<Obsolete("deprecated")>]
[<Serde.FS.SerdeRename("NewName")>]
type X = { A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Attributes.Length, Is.EqualTo(2))
    let names = t.Attributes |> List.map (fun a -> a.Name)
    Assert.That(names, Does.Contain("ObsoleteAttribute"))
    Assert.That(names, Does.Contain("Serde.FS.SerdeRenameAttribute"))

[<Test>]
let ``Extracts enum case attributes`` () =
    let source = """
namespace TestNs

type Color =
    | [<Serde.FS.SerdeRename("Crimson")>] Red = 1
    | Blue = 2
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    match types.[0].Kind with
    | Enum cases ->
        Assert.That(cases.Length, Is.EqualTo(2))
        Assert.That(cases.[0].CaseName, Is.EqualTo("Red"))
        Assert.That(cases.[0].Value, Is.EqualTo(1))
        Assert.That(cases.[0].Attributes.Length, Is.EqualTo(1))
        Assert.That(cases.[0].Attributes.[0].Name, Is.EqualTo("Serde.FS.SerdeRenameAttribute"))
        Assert.That(cases.[0].Attributes.[0].ConstructorArgs, Is.EqualTo([box "Crimson"]))
        Assert.That(cases.[1].CaseName, Is.EqualTo("Blue"))
        Assert.That(cases.[1].Attributes, Is.Empty)
    | other -> Assert.Fail(sprintf "Expected Enum kind, got %A" other)

[<Test>]
let ``Attribute with no args produces empty ConstructorArgs and NamedArgs`` () =
    let source = """
namespace TestNs

[<Serde>]
type X = { A: int }
"""
    let types = TypeKindExtractor.extractTypes "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let t = types.[0]
    Assert.That(t.Attributes.Length, Is.EqualTo(1))
    Assert.That(t.Attributes.[0].Name, Is.EqualTo("SerdeAttribute"))
    Assert.That(t.Attributes.[0].ConstructorArgs, Is.Empty)
    Assert.That(t.Attributes.[0].NamedArgs, Is.Empty)
