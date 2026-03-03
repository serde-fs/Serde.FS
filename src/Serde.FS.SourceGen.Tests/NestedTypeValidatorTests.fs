module Serde.FS.SourceGen.Tests.NestedTypeValidatorTests

open NUnit.Framework
open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS
open Serde.FS.SourceGen

let private mkTypeInfo ns modules name kind : TypeInfo =
    { Namespace = ns; EnclosingModules = modules; TypeName = name; Kind = kind; Attributes = [] }

let private mkPrimitive name prim : TypeInfo =
    mkTypeInfo None [] name (Primitive prim)

let private mkRecordRef ns modules name : TypeInfo =
    mkTypeInfo ns modules name (Record [])

let private mkUnionRef ns modules name : TypeInfo =
    mkTypeInfo ns modules name (Union [])

let private mkEnumRef ns modules name : TypeInfo =
    mkTypeInfo ns modules name (Enum [])

let private mkField name ty : SerdeFieldInfo =
    { Name = name; RawName = name; Type = ty; Attributes = SerdeAttributes.empty; Capability = Both }

let private mkUnionCase caseName fields : SerdeUnionCaseInfo =
    { CaseName = caseName; RawCaseName = caseName; Fields = fields; Tag = None; Attributes = SerdeAttributes.empty }

let private mkSerdeType ns modules name fields unionCases : SerdeTypeInfo =
    {
        Raw = mkTypeInfo ns modules name (Record [])
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        Fields = fields
        UnionCases = unionCases
        EnumCases = None
    }

[<Test>]
let ``Record with nested unmarked record field produces error`` () =
    let addressTi = mkRecordRef (Some "MyApp") [] "Address"
    let personType = mkSerdeType (Some "MyApp") [] "Person" (Some [ mkField "Home" addressTi ]) None
    let serdeNames = set [ "MyApp.Person" ]
    let errors = NestedTypeValidator.validate serdeNames [ personType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("MyApp.Address"))

[<Test>]
let ``Union case with unmarked record payload produces error`` () =
    let nameTi = mkRecordRef None [ "Program" ] "Name"
    let petType =
        { Raw = mkTypeInfo None [ "Program" ] "Pet" (Union [])
          Capability = Both
          Attributes = SerdeAttributes.empty
          ConverterType = None
          Fields = None
          UnionCases = Some [ mkUnionCase "Dog" [ mkField "Item" nameTi ] ]
          EnumCases = None }
    let serdeNames = set [ "Program.Pet" ]
    let errors = NestedTypeValidator.validate serdeNames [ petType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("Program.Name"))

[<Test>]
let ``Deeply nested: A references B references C where C is unmarked`` () =
    // B has a field of type C (unmarked), A has a field of type B (marked)
    let cTi = mkRecordRef (Some "App") [] "C"
    let bType = mkSerdeType (Some "App") [] "B" (Some [ mkField "Inner" cTi ]) None
    let bTi = mkRecordRef (Some "App") [] "B"
    let aType = mkSerdeType (Some "App") [] "A" (Some [ mkField "Nested" bTi ]) None
    let serdeNames = set [ "App.A"; "App.B" ]
    let errors = NestedTypeValidator.validate serdeNames [ aType; bType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("App.C"))

[<Test>]
let ``All types have Serde produces no errors`` () =
    let addressTi = mkRecordRef (Some "MyApp") [] "Address"
    let personType = mkSerdeType (Some "MyApp") [] "Person" (Some [ mkField "Home" addressTi ]) None
    let serdeNames = set [ "MyApp.Person"; "MyApp.Address" ]
    let errors = NestedTypeValidator.validate serdeNames [ personType ]
    Assert.That(errors, Is.Empty)

[<Test>]
let ``Primitive-only fields produce no errors`` () =
    let personType =
        mkSerdeType (Some "MyApp") [] "Person"
            (Some [
                mkField "Name" (mkPrimitive "string" String)
                mkField "Age" (mkPrimitive "int" Int32)
            ])
            None
    let serdeNames = set [ "MyApp.Person" ]
    let errors = NestedTypeValidator.validate serdeNames [ personType ]
    Assert.That(errors, Is.Empty)

[<Test>]
let ``List of unmarked type produces error`` () =
    let itemTi = mkRecordRef (Some "App") [] "Item"
    let listTi = mkTypeInfo None [] "Item list" (List itemTi)
    let orderType = mkSerdeType (Some "App") [] "Order" (Some [ mkField "Items" listTi ]) None
    let serdeNames = set [ "App.Order" ]
    let errors = NestedTypeValidator.validate serdeNames [ orderType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("App.Item"))

[<Test>]
let ``Option of unmarked type produces error`` () =
    let addressTi = mkRecordRef (Some "App") [] "Address"
    let optTi = mkTypeInfo None [] "Address option" (Option addressTi)
    let personType = mkSerdeType (Some "App") [] "Person" (Some [ mkField "Home" optTi ]) None
    let serdeNames = set [ "App.Person" ]
    let errors = NestedTypeValidator.validate serdeNames [ personType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("App.Address"))

[<Test>]
let ``Map with unmarked value type produces error`` () =
    let keyTi = mkPrimitive "string" String
    let valTi = mkRecordRef (Some "App") [] "Config"
    let mapTi = mkTypeInfo None [] "Map" (Map (keyTi, valTi))
    let settingsType = mkSerdeType (Some "App") [] "Settings" (Some [ mkField "Configs" mapTi ]) None
    let serdeNames = set [ "App.Settings" ]
    let errors = NestedTypeValidator.validate serdeNames [ settingsType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("App.Config"))

[<Test>]
let ``Tuple of primitives produces no errors`` () =
    let tupleTi =
        mkTypeInfo None [] "int * string"
            (Tuple [
                { Name = "Item1"; Type = mkPrimitive "int" Int32; Attributes = [] }
                { Name = "Item2"; Type = mkPrimitive "string" String; Attributes = [] }
            ])
    let myType = mkSerdeType (Some "App") [] "MyType" (Some [ mkField "Pair" tupleTi ]) None
    let serdeNames = set [ "App.MyType" ]
    let errors = NestedTypeValidator.validate serdeNames [ myType ]
    Assert.That(errors, Is.Empty)

[<Test>]
let ``Marked enum field produces no errors`` () =
    let colorTi = mkEnumRef (Some "App") [] "Color"
    let shapeType = mkSerdeType (Some "App") [] "Shape" (Some [ mkField "Color" colorTi ]) None
    let serdeNames = set [ "App.Shape"; "App.Color" ]
    let errors = NestedTypeValidator.validate serdeNames [ shapeType ]
    Assert.That(errors, Is.Empty)

[<Test>]
let ``Unmarked enum field produces error`` () =
    let colorTi = mkEnumRef (Some "App") [] "Color"
    let shapeType = mkSerdeType (Some "App") [] "Shape" (Some [ mkField "Color" colorTi ]) None
    let serdeNames = set [ "App.Shape" ]
    let errors = NestedTypeValidator.validate serdeNames [ shapeType ]
    Assert.That(errors.Length, Is.EqualTo(1))
    Assert.That(errors.[0], Does.Contain("App.Color"))
