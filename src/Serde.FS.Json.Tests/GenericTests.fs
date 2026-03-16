module Serde.FS.Json.Tests.GenericTests

open NUnit.Framework
open Serde.FS
open FSharp.SourceDjinn.TypeModel
open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS.Json
open Serde.FS.Json.SourceGen

let private emitter = JsonCodeEmitter() :> ISerdeCodeEmitter
let private resolverEmitter = JsonCodeEmitter() :> ISerdeResolverEmitter

let private mkPrimType name kind : TypeInfo =
    { Namespace = None; EnclosingModules = []; TypeName = name; Kind = Primitive kind; Attributes = []
      GenericParameters = []; GenericArguments = [] }

let private mkField name typeName kind : SerdeFieldInfo =
    { Name = name; RawName = name; Type = mkPrimType typeName kind; Attributes = SerdeAttributes.empty; Capability = Both; CodecType = None }

/// Build a generic union definition: Wrapper<'T> = Wrapper of 'T
let private mkWrapperDef ns : SerdeTypeInfo =
    let wrapperField : FieldInfo = { Name = "Item"; Type = { Namespace = None; EnclosingModules = []; TypeName = "T"; Kind = GenericParameter "T"; Attributes = []; GenericParameters = []; GenericArguments = [] }; Attributes = [] }
    let wrapperCase : UnionCase = { CaseName = "Wrapper"; Fields = [wrapperField]; Tag = Some 0; Attributes = [] }
    let wrapperTi : TypeInfo = {
        Namespace = ns; EnclosingModules = []; TypeName = "Wrapper"
        Kind = Union [wrapperCase]; Attributes = []
        GenericParameters = [{ Name = "T"; Constraints = [] }]; GenericArguments = []
    }
    let sti = SerdeMetadataBuilder.buildSerdeTypeInfo wrapperTi
    sti

/// Build a Person record TypeInfo
let private mkPersonTi ns : TypeInfo =
    { Namespace = ns; EnclosingModules = []; TypeName = "Person"
      Kind = Record [{ Name = "Name"; Type = mkPrimType "string" String; Attributes = [] }]
      Attributes = []; GenericParameters = []; GenericArguments = [] }

/// Build a constructed Wrapper<Person> SerdeTypeInfo by instantiating the definition
let private mkConstructedWrapperPerson ns =
    let wrapperDef = mkWrapperDef ns
    let personTi = mkPersonTi ns
    let instantiated = TypeInfo.instantiate wrapperDef.Raw [personTi]
    let instantiated = { instantiated with Namespace = ns }
    let baseInfo = SerdeMetadataBuilder.buildSerdeTypeInfo instantiated
    { baseInfo with
        GenericContext = Some {
            DefinitionType = wrapperDef.Raw
            GenericParameters = wrapperDef.Raw.GenericParameters
            GenericArguments = [personTi]
        } }

[<Test>]
let ``Emits codec for generic union Wrapper of Person`` () =
    let info = mkConstructedWrapperPerson (Some "MyApp")
    let code = emitter.Emit(info)
    // Module and type names use underscore separation
    Assert.That(code, Does.Contain("module rec Serde.Generated.Wrapper_Person"))
    Assert.That(code, Does.Contain("wrapper_PersonJsonCodec"))
    // The typeof and codec type use the constructed generic FQN
    Assert.That(code, Does.Contain("IJsonCodec<MyApp.Wrapper<MyApp.Person>>"))
    // Case construction qualifies with namespace only (not the generic type)
    Assert.That(code, Does.Contain("MyApp.Wrapper("))

[<Test>]
let ``Emits codec for nested generic Wrapper of Wrapper of Person`` () =
    let ns = Some "MyApp"
    let wrapperDef = mkWrapperDef ns
    let personTi = mkPersonTi ns
    // Inner: Wrapper<Person>
    let innerConstructed = TypeInfo.instantiate wrapperDef.Raw [personTi]
    let innerConstructed = { innerConstructed with Namespace = ns }
    // Outer: Wrapper<Wrapper<Person>>
    let outerConstructed = TypeInfo.instantiate wrapperDef.Raw [innerConstructed]
    let outerConstructed = { outerConstructed with Namespace = ns }
    let baseInfo = SerdeMetadataBuilder.buildSerdeTypeInfo outerConstructed
    let info = { baseInfo with
                    GenericContext = Some {
                        DefinitionType = wrapperDef.Raw
                        GenericParameters = wrapperDef.Raw.GenericParameters
                        GenericArguments = [innerConstructed]
                    } }
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.Wrapper_WrapperPerson"))

[<Test>]
let ``Generic record Box of int emits correct code`` () =
    let ns = Some "MyApp"
    let boxField : FieldInfo = { Name = "Value"; Type = { Namespace = None; EnclosingModules = []; TypeName = "T"; Kind = GenericParameter "T"; Attributes = []; GenericParameters = []; GenericArguments = [] }; Attributes = [] }
    let boxDef : TypeInfo = {
        Namespace = ns; EnclosingModules = []; TypeName = "Box"
        Kind = Record [boxField]; Attributes = []
        GenericParameters = [{ Name = "T"; Constraints = [] }]; GenericArguments = []
    }
    let intTi = mkPrimType "int" Int32
    let instantiated = TypeInfo.instantiate boxDef [intTi]
    let instantiated = { instantiated with Namespace = ns }
    let baseInfo = SerdeMetadataBuilder.buildSerdeTypeInfo instantiated
    let info = { baseInfo with
                    GenericContext = Some {
                        DefinitionType = boxDef
                        GenericParameters = boxDef.GenericParameters
                        GenericArguments = [intTi]
                    } }
    let code = emitter.Emit(info)
    Assert.That(code, Does.Contain("module rec Serde.Generated.Box_Int"))
    Assert.That(code, Does.Contain("box_IntJsonCodec"))
    Assert.That(code, Does.Contain("IJsonCodec<MyApp.Box<int>>"))
    Assert.That(code, Does.Contain("JsonValue.Object"))

[<Test>]
let ``EmitResolver includes constructed generic types`` () =
    let personInfo : SerdeTypeInfo = {
        Raw = mkPersonTi (Some "MyApp")
        Capability = Both
        Attributes = SerdeAttributes.empty
        ConverterType = None
        CodecType = None
        Fields = Some [ mkField "Name" "string" String ]
        UnionCases = None
        EnumCases = None
        GenericContext = None
    }
    let wrapperInfo = mkConstructedWrapperPerson (Some "MyApp")
    let types = [ personInfo; wrapperInfo ]

    let result = resolverEmitter.EmitResolver(types)
    Assert.That(result.IsSome, Is.True)
    let code = result.Value
    Assert.That(code, Does.Contain("typeof<MyApp.Wrapper<MyApp.Person>>"))
    Assert.That(code, Does.Contain("wrapper_PersonJsonCodec"))
    Assert.That(code, Does.Contain("let private register"))
