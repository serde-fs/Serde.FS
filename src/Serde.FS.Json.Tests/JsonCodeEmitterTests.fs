module Serde.FS.Json.Tests.JsonCodeEmitterTests

open NUnit.Framework
open Serde.FS
open FSharp.SourceDjinn.TypeModel
open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS.Json
open Serde.FS.Json.SourceGen

let private emitter = JsonCodeEmitter() :> ISerdeCodeEmitter
let private resolverEmitter = JsonCodeEmitter() :> ISerdeResolverEmitter

/// Helper to build a simple field TypeInfo from a type name and primitive kind.
let private mkPrimType name kind : TypeInfo =
    { Namespace = None; EnclosingModules = []; TypeName = name; Kind = Primitive kind; Attributes = []; GenericParameters = []; GenericArguments = [] }

/// Helper to build a SerdeFieldInfo from a name and a simple primitive type.
let private mkField name typeName kind : SerdeFieldInfo =
    { Name = name; RawName = name; Type = mkPrimType typeName kind; Attributes = SerdeAttributes.empty; Capability = Both; CodecType = None }

/// Helper to build a SerdeFieldInfo with a full TypeInfo.
let private mkFieldWithType name (typeInfo: TypeInfo) : SerdeFieldInfo =
    { Name = name; RawName = name; Type = typeInfo; Attributes = SerdeAttributes.empty; Capability = Both; CodecType = None }

/// Helper to build an option TypeInfo wrapping an inner TypeInfo.
let private mkOptionType (inner: TypeInfo) : TypeInfo =
    { Namespace = None; EnclosingModules = []; TypeName = "option"; Kind = Option inner; Attributes = []; GenericParameters = []; GenericArguments = [] }

/// Helper to build a SerdeTypeInfo for an option type.
let private mkOptionInfo (inner: TypeInfo) : SerdeTypeInfo =
    let optType = mkOptionType inner
    {
        Raw = optType
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = None
        UnionCases = None
        EnumCases = None
        GenericContext = None
    }

/// Helper to build a SerdeTypeInfo for a simple record.
let private mkRecordInfo ns typeName cap (fields: SerdeFieldInfo list) : SerdeTypeInfo =
    let rawFields =
        fields |> List.map (fun f -> { Name = f.RawName; Type = f.Type; Attributes = [] } : Types.FieldInfo)
    {
        Raw = {
            Namespace = ns
            EnclosingModules = []
            TypeName = typeName
            Kind = Record rawFields
            Attributes = []
            GenericParameters = []
            GenericArguments = []
        }
        Capability = cap
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = Some fields
        UnionCases = None
        EnumCases = None
        GenericContext = None
    }

[<Test>]
let ``Emits valid F# for simple record`` () =
    let info = mkRecordInfo (Some "MyApp") "Person" Both [
        mkField "FName" "string" String
        mkField "LName" "string" String
        mkField "Age" "int" Int32
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.Person"))
    Assert.That(code, Does.Contain("module internal PersonSerdeCodec"))
    Assert.That(code, Does.Contain("personJsonCodec"))
    Assert.That(code, Does.Contain("IJsonCodec<MyApp.Person>"))
    Assert.That(code, Does.Contain("JsonValue.Object"))
    Assert.That(code, Does.Contain("CodecResolver.resolve"))
    Assert.That(code, Does.Contain("\"FName\""))
    Assert.That(code, Does.Contain("\"LName\""))
    Assert.That(code, Does.Contain("\"Age\""))

[<Test>]
let ``Emits codec encode for bool fields`` () =
    let info = mkRecordInfo (Some "MyApp") "Config" Serialize [
        mkField "IsEnabled" "bool" Bool
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("\"IsEnabled\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve"))

[<Test>]
let ``Emits codec encode for numeric types`` () =
    let info = mkRecordInfo (Some "MyApp") "Numbers" Serialize [
        mkField "I" "int" Int32
        mkField "I64" "int64" Int64
        mkField "F" "float" Float64
        mkField "D" "decimal" Decimal
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("\"I\""))
    Assert.That(code, Does.Contain("\"I64\""))
    Assert.That(code, Does.Contain("\"F\""))
    Assert.That(code, Does.Contain("\"D\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve"))

[<Test>]
let ``Emits option handling for optional fields`` () =
    let optionType = {
        Namespace = None; EnclosingModules = []; TypeName = "option"
        Kind = Option (mkPrimType "string" String); Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkRecordInfo (Some "MyApp") "Person" Serialize [
        mkFieldWithType "MiddleName" optionType
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("match value.MiddleName with"))
    Assert.That(code, Does.Contain("| Some v ->"))
    Assert.That(code, Does.Contain("| None ->"))
    Assert.That(code, Does.Contain("JsonValue.Null"))

[<Test>]
let ``Emits auto-generated header`` () =
    let info = mkRecordInfo (Some "MyApp") "Foo" Both [
        mkField "X" "int" Int32
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.StartWith("// <auto-generated />"))

[<Test>]
let ``Emits DateTime field via CodecResolver`` () =
    let info = mkRecordInfo (Some "MyApp") "Event" Serialize [
        mkField "CreatedAt" "System.DateTime" DateTime
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("\"CreatedAt\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<System.DateTime>"))

[<Test>]
let ``Emits Guid field via CodecResolver`` () =
    let info = mkRecordInfo (Some "MyApp") "Entity" Serialize [
        mkField "Id" "System.Guid" Guid
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("\"Id\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<System.Guid>"))

[<Test>]
let ``Full pipeline: parse then emit`` () =
    let source = """
namespace TestApp

[<Serde>]
type Person = { FName: string; LName: string; Age: int }
"""
    let types = Serde.FS.SourceGen.SerdeAstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let code = emitter.Emit(types.[0])
    Assert.That(code, Does.Contain("module rec Serde.Generated.Person"))
    Assert.That(code, Does.Contain("personJsonCodec"))
    Assert.That(code, Does.Contain("IJsonCodec<TestApp.Person>"))
    Assert.That(code, Does.Contain("\"FName\""))
    Assert.That(code, Does.Contain("\"LName\""))
    Assert.That(code, Does.Contain("\"Age\""))

[<Test>]
let ``EmitResolver produces valid consolidated file for multiple types`` () =
    let types = [
        mkRecordInfo (Some "MyApp") "Person" Both [
            mkField "FName" "string" String
            mkField "LName" "string" String
        ]
        mkRecordInfo (Some "MyApp") "Address" Serialize [
            mkField "Street" "string" String
        ]
    ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("namespace Serde.Generated"))
    Assert.That(code, Does.Contain("module SerdeJsonCodecs"))
    Assert.That(code, Does.Contain("typeof<MyApp.Person>"))
    Assert.That(code, Does.Contain("typeof<MyApp.Address>"))
    Assert.That(code, Does.Contain("personJsonCodec"))
    Assert.That(code, Does.Contain("addressJsonCodec"))
    Assert.That(code, Does.Contain("CodecRegistry.add"))
    Assert.That(code, Does.Contain("let private register"))
    Assert.That(code, Does.Contain("SerdeJson.registerCodecs register"))

[<Test>]
let ``EmitResolver returns None for empty list`` () =
    let result = resolverEmitter.EmitResolver([])
    Assert.That(result.IsNone, Is.True)

[<Test>]
let ``Emits codec for option int`` () =
    let info = mkOptionInfo (mkPrimType "int" Int32)
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IJsonCodec<int option>"))
    Assert.That(code, Does.Contain("JsonValue.Null"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int>"))
    Assert.That(code, Does.Contain("intOptionJsonCodec"))

[<Test>]
let ``Emits codec for option string`` () =
    let info = mkOptionInfo (mkPrimType "string" String)
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IJsonCodec<string option>"))
    Assert.That(code, Does.Not.Contain("intOptionJsonCodec"), "Should not contain int references")
    Assert.That(code, Does.Contain("stringOptionJsonCodec"))

[<Test>]
let ``Emits codec for option record`` () =
    let personType : TypeInfo = {
        Namespace = Some "MyApp"; EnclosingModules = []; TypeName = "Person"
        Kind = Record []; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkOptionInfo personType
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IJsonCodec<MyApp.Person option>"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<MyApp.Person>"))

[<Test>]
let ``Emits codec for nested option option int`` () =
    let innerOption = mkOptionType (mkPrimType "int" Int32)
    let info = mkOptionInfo innerOption
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IJsonCodec<int option option>"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int option>"))

[<Test>]
let ``EmitResolver with mixed record and option types`` () =
    let recordType = mkRecordInfo (Some "MyApp") "Person" Both [
        mkField "FName" "string" String
    ]
    let optionType = mkOptionInfo (mkPrimType "string" String)
    let types = [ recordType; optionType ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("typeof<MyApp.Person>"))
    Assert.That(code, Does.Contain("typeof<string option>"))
    Assert.That(code, Does.Contain("personJsonCodec"))
    Assert.That(code, Does.Contain("stringOptionJsonCodec"))
    Assert.That(code, Does.Contain("let private register"))
    Assert.That(code, Does.Contain("SerdeJson.registerCodecs register"))

[<Test>]
let ``Emits fully-qualified type for nested record field`` () =
    let addressType : TypeInfo = {
        Namespace = Some "My.App"; EnclosingModules = []; TypeName = "Address"
        Kind = Record []; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkRecordInfo (Some "My.App") "User" Both [
        mkField "Name" "string" String
        mkFieldWithType "Address" addressType
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("typeof<My.App.Address>"), "Should use FQ name for codec resolve")
    Assert.That(code, Does.Contain("My.App.User"), "Should use FQ name for the record type")

[<Test>]
let ``Emits fully-qualified type for nested record from another module`` () =
    let addressType : TypeInfo = {
        Namespace = Some "Outer"; EnclosingModules = ["Inner"]; TypeName = "Address"
        Kind = Record []; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkRecordInfo (Some "My.App") "User" Both [
        mkFieldWithType "Address" addressType
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("typeof<Outer.Inner.Address>"), "Should use FQ name with module for codec resolve")

[<Test>]
let ``Emits option handling for nested record option field`` () =
    let addressType : TypeInfo = {
        Namespace = Some "My.App"; EnclosingModules = []; TypeName = "Address"
        Kind = Record []; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let optionType = {
        Namespace = None; EnclosingModules = []; TypeName = "option"
        Kind = Option addressType; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkRecordInfo (Some "My.App") "User" Both [
        mkFieldWithType "Address" optionType
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("match value.Address with"), "Should emit option match")
    Assert.That(code, Does.Contain("| Some v ->"), "Should emit Some case")
    Assert.That(code, Does.Contain("CodecResolver.resolve"), "Should resolve codec for nested record")

/// Helper to build a tuple TypeInfo from a list of element TypeInfos.
let private mkTupleType (elements: TypeInfo list) : TypeInfo =
    let fields = elements |> List.mapi (fun i ti -> { Name = sprintf "Item%d" (i+1); Type = ti; Attributes = [] } : Types.FieldInfo)
    { Namespace = None; EnclosingModules = []; TypeName = "tuple"; Kind = Tuple fields; Attributes = []; GenericParameters = []; GenericArguments = [] }

/// Helper to build a SerdeTypeInfo for a tuple type.
let private mkTupleInfo (elements: TypeInfo list) : SerdeTypeInfo =
    let tupType = mkTupleType elements
    {
        Raw = tupType
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = None
        UnionCases = None
        EnumCases = None
        GenericContext = None
    }

[<Test>]
let ``Emits codec for simple int * string tuple`` () =
    let info = mkTupleInfo [ mkPrimType "int" Int32; mkPrimType "string" String ]
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IJsonCodec<(int * string)>"))
    Assert.That(code, Does.Contain("JsonValue.Array"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int>"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<string>"))
    Assert.That(code, Does.Contain("module rec Serde.Generated.IntStringTuple"))
    Assert.That(code, Does.Contain("intStringTupleJsonCodec"))

[<Test>]
let ``Emits codec for nested tuple (int * int) * string`` () =
    let innerTuple = mkTupleType [ mkPrimType "int" Int32; mkPrimType "int" Int32 ]
    let info = mkTupleInfo [ innerTuple; mkPrimType "string" String ]
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("IntIntTupleStringTuple"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<(int * int)>"))

[<Test>]
let ``Emits codec for tuple containing record`` () =
    let addressType : TypeInfo = {
        Namespace = Some "My.App"; EnclosingModules = []; TypeName = "Address"
        Kind = Record []; Attributes = []
        GenericParameters = []; GenericArguments = []
    }
    let info = mkTupleInfo [ mkPrimType "int" Int32; addressType ]
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<My.App.Address>"))

[<Test>]
let ``EmitResolver with mixed record option and tuple types`` () =
    let recordType = mkRecordInfo (Some "MyApp") "Person" Both [
        mkField "FName" "string" String
    ]
    let optionType = mkOptionInfo (mkPrimType "string" String)
    let tupleType = mkTupleInfo [ mkPrimType "int" Int32; mkPrimType "string" String ]
    let types = [ recordType; optionType; tupleType ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("typeof<MyApp.Person>"))
    Assert.That(code, Does.Contain("typeof<string option>"))
    Assert.That(code, Does.Contain("typeof<(int * string)>"))
    Assert.That(code, Does.Contain("personJsonCodec"))
    Assert.That(code, Does.Contain("stringOptionJsonCodec"))
    Assert.That(code, Does.Contain("intStringTupleJsonCodec"))
    Assert.That(code, Does.Contain("let private register"))
    Assert.That(code, Does.Contain("SerdeJson.registerCodecs register"))

[<Test>]
let ``typeInfoToPascalName produces IntStringTuple`` () =
    let ti = mkTupleType [ mkPrimType "int" Int32; mkPrimType "string" String ]
    let name = Types.typeInfoToPascalName ti
    Assert.That(name, Is.EqualTo("IntStringTuple"))

[<Test>]
let ``typeInfoToFqFSharpType produces parenthesized tuple`` () =
    let ti = mkTupleType [ mkPrimType "int" Int32; mkPrimType "string" String ]
    let fq = Types.typeInfoToFqFSharpType ti
    Assert.That(fq, Is.EqualTo("(int * string)"))

[<Test>]
let ``typeInfoToFqFSharpType produces parenthesized tuple nested in option`` () =
    let ti = mkTupleType [ mkPrimType "int" Int32; mkPrimType "string" String ]
    let optTi = mkOptionType ti
    let fq = Types.typeInfoToFqFSharpType optTi
    Assert.That(fq, Is.EqualTo("(int * string) option"))

// --- Enum tests ---

/// Helper to build a SerdeTypeInfo for an enum type.
let private mkEnumInfo ns typeName (cases: SerdeEnumCaseInfo list) : SerdeTypeInfo =
    let rawCases =
        cases |> List.map (fun c ->
            { CaseName = c.RawCaseName; Value = c.Value; Attributes = [] } : Types.EnumCase)
    {
        Raw = {
            Namespace = ns
            EnclosingModules = []
            TypeName = typeName
            Kind = Enum rawCases
            Attributes = []
            GenericParameters = []
            GenericArguments = []
        }
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = None
        UnionCases = None
        EnumCases = Some cases
        GenericContext = None
    }

/// Helper to build a SerdeEnumCaseInfo.
let private mkEnumCase name value : SerdeEnumCaseInfo =
    { CaseName = name; RawCaseName = name; Value = value; Attributes = SerdeAttributes.empty; Capability = Both }

/// Helper to build a SerdeEnumCaseInfo with a rename.
let private mkEnumCaseRenamed rawName effectiveName value : SerdeEnumCaseInfo =
    { CaseName = effectiveName; RawCaseName = rawName; Value = value
      Attributes = { SerdeAttributes.empty with Rename = Some effectiveName }; Capability = Both }

[<Test>]
let ``Emits codec for basic enum`` () =
    let info = mkEnumInfo (Some "TestNs") "Color" [
        mkEnumCase "Red" 1
        mkEnumCase "Blue" 2
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.Color"))
    Assert.That(code, Does.Contain("IJsonCodec<TestNs.Color>"))
    Assert.That(code, Does.Contain("""if s = "Red" then TestNs.Color.Red"""))
    Assert.That(code, Does.Contain("""elif s = "Blue" then TestNs.Color.Blue"""))
    Assert.That(code, Does.Contain("if value = TestNs.Color.Red then JsonValue.String \"Red\""))
    Assert.That(code, Does.Contain("elif value = TestNs.Color.Blue then JsonValue.String \"Blue\""))

[<Test>]
let ``Emits codec for enum with renamed case`` () =
    let info = mkEnumInfo (Some "TestNs") "Color" [
        mkEnumCaseRenamed "Red" "Crimson" 1
        mkEnumCase "Blue" 2
    ]

    let code = emitter.Emit(info)
    // Deserialization uses effective name
    Assert.That(code, Does.Contain("""if s = "Crimson" then TestNs.Color.Red"""))
    // Serialization compares raw case, writes effective name
    Assert.That(code, Does.Contain("if value = TestNs.Color.Red then JsonValue.String \"Crimson\""))
    Assert.That(code, Does.Contain("""elif s = "Blue" then TestNs.Color.Blue"""))

[<Test>]
let ``EmitResolver includes enum type`` () =
    let enumInfo = mkEnumInfo (Some "TestNs") "Color" [
        mkEnumCase "Red" 1
        mkEnumCase "Blue" 2
    ]
    let recordInfo = mkRecordInfo (Some "TestNs") "Person" Both [
        mkField "Name" "string" String
    ]
    let types = [ recordInfo; enumInfo ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("typeof<TestNs.Color>"))
    Assert.That(code, Does.Contain("colorJsonCodec"))
    Assert.That(code, Does.Contain("let private register"))

[<Test>]
let ``Full pipeline: parse then emit enum`` () =
    let source = """
namespace TestApp

[<Serde>]
type Color =
    | Red = 1
    | Green = 2
    | Blue = 3
"""
    let types = Serde.FS.SourceGen.SerdeAstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let code = emitter.Emit(types.[0])
    Assert.That(code, Does.Contain("module rec Serde.Generated.Color"))
    Assert.That(code, Does.Contain("IJsonCodec<TestApp.Color>"))
    Assert.That(code, Does.Contain("""if s = "Red" then TestApp.Color.Red"""))
    Assert.That(code, Does.Contain("""elif s = "Green" then TestApp.Color.Green"""))
    Assert.That(code, Does.Contain("""elif s = "Blue" then TestApp.Color.Blue"""))

// --- Union tests ---

/// Helper to build a SerdeUnionCaseInfo.
let private mkUnionCase name (fields: SerdeFieldInfo list) : SerdeUnionCaseInfo =
    { CaseName = name; RawCaseName = name; Fields = fields; Tag = None; Attributes = SerdeAttributes.empty }

/// Helper to build a SerdeUnionCaseInfo with a rename.
let private mkUnionCaseRenamed rawName effectiveName (fields: SerdeFieldInfo list) : SerdeUnionCaseInfo =
    { CaseName = effectiveName; RawCaseName = rawName; Fields = fields; Tag = None
      Attributes = { SerdeAttributes.empty with Rename = Some effectiveName } }

/// Helper to build a skipped SerdeUnionCaseInfo.
let private mkUnionCaseSkipped name (fields: SerdeFieldInfo list) : SerdeUnionCaseInfo =
    { CaseName = name; RawCaseName = name; Fields = fields; Tag = None
      Attributes = { SerdeAttributes.empty with Skip = true } }

/// Helper to build an unnamed (tuple-like) field for a union case.
let private mkUnnamedField (typeInfo: TypeInfo) : SerdeFieldInfo =
    { Name = "Item"; RawName = "Item"; Type = typeInfo; Attributes = SerdeAttributes.empty; Capability = Both; CodecType = None }

/// Helper to build a SerdeTypeInfo for a union type.
let private mkUnionInfo ns typeName (cases: SerdeUnionCaseInfo list) : SerdeTypeInfo =
    let rawCases =
        cases |> List.map (fun c ->
            { CaseName = c.RawCaseName
              Fields = c.Fields |> List.map (fun f -> { Name = f.RawName; Type = f.Type; Attributes = [] } : Types.FieldInfo)
              Tag = c.Tag
              Attributes = [] } : Types.UnionCase)
    {
        Raw = {
            Namespace = ns
            EnclosingModules = []
            TypeName = typeName
            Kind = Union rawCases
            Attributes = []
            GenericParameters = []
            GenericArguments = []
        }
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = None
        UnionCases = Some cases
        EnumCases = None
        GenericContext = None
    }

[<Test>]
let ``Emits codec for union with nullary case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCase "Empty" []
    ]

    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.MyUnion"))
    Assert.That(code, Does.Contain("IJsonCodec<TestNs.MyUnion>"))
    // Multi-case format (1 case, 0 fields -> MultiCaseUnion)
    Assert.That(code, Does.Contain("\"Case\""))
    Assert.That(code, Does.Contain("\"Fields\""))
    Assert.That(code, Does.Contain("JsonValue.Array []"))
    Assert.That(code, Does.Contain("TestNs.MyUnion.Empty"))

[<Test>]
let ``Emits codec for union with single-field case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCase "Value" [ mkField "Value" "int" Int32 ]
    ]

    let code = emitter.Emit(info)
    // Wrapper format: { "Value": <payload> }
    Assert.That(code, Does.Contain("\"Value\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int>"))
    Assert.That(code, Does.Contain("TestNs.MyUnion.Value"))

[<Test>]
let ``Emits codec for union with tuple case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCase "Pair" [
            mkUnnamedField (mkPrimType "float" Float64)
            mkUnnamedField (mkPrimType "float" Float64)
        ]
    ]

    let code = emitter.Emit(info)
    // Multi-case format (1 case, 2 fields -> MultiCaseUnion)
    Assert.That(code, Does.Contain("\"Case\""))
    Assert.That(code, Does.Contain("\"Fields\""))
    Assert.That(code, Does.Contain("JsonValue.Array"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<float>"))
    Assert.That(code, Does.Contain("TestNs.MyUnion.Pair(e0, e1)"))

[<Test>]
let ``Emits codec for union with record-like case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCase "Person" [
            mkField "Name" "string" String
            mkField "Age" "int" Int32
        ]
    ]

    let code = emitter.Emit(info)
    // Multi-case format (1 case, 2 named fields -> MultiCaseUnion)
    Assert.That(code, Does.Contain("\"Case\""))
    Assert.That(code, Does.Contain("\"Fields\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<string>"))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int>"))
    Assert.That(code, Does.Contain("TestNs.MyUnion.Person(e0, e1)"))

[<Test>]
let ``Emits codec for union with renamed case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCaseRenamed "A" "Alpha" []
    ]

    let code = emitter.Emit(info)
    // Multi-case format (1 case, 0 fields -> MultiCaseUnion)
    // JSON uses effective name in Case property
    Assert.That(code, Does.Contain("\"Alpha\""))
    Assert.That(code, Does.Contain("""caseName = "Alpha" then"""))
    // F# construction uses raw name
    Assert.That(code, Does.Contain("TestNs.MyUnion.A"))

[<Test>]
let ``Emits codec for union with mixed case shapes`` () =
    let info = mkUnionInfo (Some "TestNs") "Shape" [
        mkUnionCase "Point" []
        mkUnionCase "Circle" [ mkField "Radius" "float" Float64 ]
        mkUnionCase "Line" [
            mkUnnamedField (mkPrimType "float" Float64)
            mkUnnamedField (mkPrimType "float" Float64)
        ]
        mkUnionCase "Rect" [
            mkField "Width" "float" Float64
            mkField "Height" "float" Float64
        ]
    ]

    let code = emitter.Emit(info)
    // Multi-case format: all cases use Case/Fields
    Assert.That(code, Does.Contain("JsonValue.String \"Point\""))
    Assert.That(code, Does.Contain("JsonValue.String \"Circle\""))
    Assert.That(code, Does.Contain("JsonValue.String \"Line\""))
    Assert.That(code, Does.Contain("JsonValue.String \"Rect\""))
    Assert.That(code, Does.Contain("TestNs.Shape.Circle("))
    Assert.That(code, Does.Contain("TestNs.Shape.Line(e0, e1)"))
    Assert.That(code, Does.Contain("TestNs.Shape.Rect(e0, e1)"))
    // Read: if/elif chain on caseName
    Assert.That(code, Does.Contain("""if caseName = "Point" then"""))
    Assert.That(code, Does.Contain("""elif caseName = "Circle" then"""))
    Assert.That(code, Does.Contain("""elif caseName = "Line" then"""))
    Assert.That(code, Does.Contain("""elif caseName = "Rect" then"""))

[<Test>]
let ``Emits codec for union with skipped case`` () =
    let info = mkUnionInfo (Some "TestNs") "MyUnion" [
        mkUnionCase "A" []
        mkUnionCaseSkipped "B" [ mkField "X" "int" Int32 ]
    ]

    let code = emitter.Emit(info)
    // Multi-case format (2 cases -> MultiCaseUnion, classify on ALL cases)
    Assert.That(code, Does.Contain("\"A\""))
    // Skipped case excluded from if/elif chain
    Assert.That(code, Does.Not.Contain("""caseName = "B" then"""))
    // Wildcard covers skipped cases
    Assert.That(code, Does.Contain("| _ -> failwith"))
    // Active case present
    Assert.That(code, Does.Contain("""caseName = "A" then"""))

[<Test>]
let ``EmitResolver includes union type`` () =
    let unionInfo = mkUnionInfo (Some "TestNs") "Shape" [
        mkUnionCase "Circle" [ mkField "Radius" "float" Float64 ]
        mkUnionCase "Point" []
    ]
    let recordInfo = mkRecordInfo (Some "TestNs") "Person" Both [
        mkField "Name" "string" String
    ]
    let types = [ recordInfo; unionInfo ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("typeof<TestNs.Shape>"))
    Assert.That(code, Does.Contain("shapeJsonCodec"))
    Assert.That(code, Does.Contain("let private register"))

[<Test>]
let ``Full pipeline: parse then emit union`` () =
    let source = """
namespace TestApp

[<Serde>]
type Shape =
    | Circle of radius: float
    | Point
"""
    let types = Serde.FS.SourceGen.SerdeAstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))

    let code = emitter.Emit(types.[0])
    Assert.That(code, Does.Contain("module rec Serde.Generated.Shape"))
    Assert.That(code, Does.Contain("IJsonCodec<TestApp.Shape>"))
    // Multi-case format
    Assert.That(code, Does.Contain("JsonValue.String \"Circle\""))
    Assert.That(code, Does.Contain("JsonValue.String \"Point\""))
    Assert.That(code, Does.Contain("""if caseName = "Circle" then"""))
    Assert.That(code, Does.Contain("""elif caseName = "Point" then"""))
    Assert.That(code, Does.Contain("TestApp.Shape.Circle("))
    Assert.That(code, Does.Contain("TestApp.Shape.Point"))

[<Test>]
let ``Wrapper DU uses wrapper encoding`` () =
    let info = mkUnionInfo (Some "TestNs") "Box" [
        mkUnionCase "Box" [ mkField "Value" "int" Int32 ]
    ]

    let code = emitter.Emit(info)
    // Wrapper format: { "Box": <payload> }
    Assert.That(code, Does.Contain("\"Box\""))
    Assert.That(code, Does.Contain("CodecResolver.resolve typeof<int>"))
    Assert.That(code, Does.Contain("TestNs.Box.Box("))
    // Should NOT contain Case/Fields format
    Assert.That(code, Does.Not.Contain("JsonValue.String \"Box\""))

[<Test>]
let ``Single case with zero fields is MultiCase`` () =
    let info = mkUnionInfo (Some "TestNs") "Unit" [
        mkUnionCase "Unit" []
    ]

    let code = emitter.Emit(info)
    // 1 case, 0 fields -> MultiCaseUnion
    Assert.That(code, Does.Contain("JsonValue.String \"Unit\""))
    Assert.That(code, Does.Contain("\"Fields\""))

[<Test>]
let ``Single case with multiple fields is MultiCase`` () =
    let info = mkUnionInfo (Some "TestNs") "Pair" [
        mkUnionCase "Pair" [
            mkField "X" "int" Int32
            mkField "Y" "int" Int32
        ]
    ]

    let code = emitter.Emit(info)
    // 1 case, 2 fields -> MultiCaseUnion
    Assert.That(code, Does.Contain("JsonValue.String \"Pair\""))
    Assert.That(code, Does.Contain("\"Fields\""))

// --- Codec attribute tests ---

/// Helper to build a SerdeTypeInfo with a type-level codec.
let private mkCodecInfo ns typeName codecFqn : SerdeTypeInfo =
    let rawField : Types.FieldInfo =
        { Name = "Value"; Type = mkPrimType "string" String; Attributes = [] }
    {
        Raw = {
            Namespace = ns
            EnclosingModules = []
            TypeName = typeName
            Kind = Record [ rawField ]
            Attributes = []
            GenericParameters = []
            GenericArguments = []
        }
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = Some codecFqn
        Fields = Some [
            mkField "Value" "string" String
        ]
        UnionCases = None
        EnumCases = None
        GenericContext = None
    }

[<Test>]
let ``Emits codec for type with Codec attribute`` () =
    let info = mkCodecInfo (Some "TestNs") "FancyName" "TestNs.MyCodec"
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.FancyName"))
    Assert.That(code, Does.Contain("fancyNameJsonCodec"))
    Assert.That(code, Does.Contain("IJsonCodec<TestNs.FancyName>"))
    Assert.That(code, Does.Contain("MyCodec()"))

[<Test>]
let ``Full pipeline: parse then emit codec`` () =
    let source = """
namespace TestApp

type MyCodec() = class end

[<Serde(Codec = typeof<MyCodec>)>]
type FancyName = { Value: string }
"""
    let types = Serde.FS.SourceGen.SerdeAstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))
    Assert.That(types.[0].CodecType, Is.EqualTo(Some "MyCodec"))

    let code = emitter.Emit(types.[0])
    Assert.That(code, Does.Contain("module rec Serde.Generated.FancyName"))
    Assert.That(code, Does.Contain("fancyNameJsonCodec"))
    Assert.That(code, Does.Contain("IJsonCodec<TestApp.FancyName>"))
    Assert.That(code, Does.Contain("MyCodec()"))

[<Test>]
let ``Converter attribute is still parsed but ignored by emitter`` () =
    let source = """
namespace TestApp

type MyConverter() = class end

[<Serde(Converter = typeof<MyConverter>)>]
type FancyName = { Value: string }
"""
    let types = Serde.FS.SourceGen.SerdeAstParser.parseSource "/test.fs" source
    Assert.That(types.Length, Is.EqualTo(1))
    // ConverterType is still parsed for backwards compat
    Assert.That(types.[0].ConverterType, Is.EqualTo(Some "MyConverter"))
    // But CodecType is None, so emitter falls through to regular record emission
    Assert.That(types.[0].CodecType, Is.EqualTo(None))
    let code = emitter.Emit(types.[0])
    // Should emit as a normal record codec, not a custom converter
    Assert.That(code, Does.Contain("IJsonCodec<TestApp.FancyName>"))
    Assert.That(code, Does.Contain("JsonValue.Object"))
