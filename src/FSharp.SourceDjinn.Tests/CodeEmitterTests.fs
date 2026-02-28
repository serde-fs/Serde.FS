module FSharp.SourceDjinn.Tests.CodeEmitterTests

open NUnit.Framework
open Serde.FS
open Serde.FS.TypeKindTypes
open FSharp.SourceDjinn

type FakeEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) = sprintf "FAKE: %s" info.Raw.TypeName

[<Test>]
let ``CodeEmitter delegates to ISerdeCodeEmitter`` () =
    let emitter = FakeEmitter() :> ISerdeCodeEmitter
    let info = {
        Raw = {
            Namespace = Some "MyApp"
            EnclosingModules = []
            TypeName = "Person"
            Kind = Record [
                { Name = "FName"; Type = { Namespace = None; EnclosingModules = []; TypeName = "string"; Kind = Primitive String; Attributes = [] }; Attributes = [] }
            ]
            Attributes = []
        }
        Capability = Both
        Attributes = SerdeAttributes.empty
        Fields = Some [
            { Name = "FName"; RawName = "FName"; Type = { Namespace = None; EnclosingModules = []; TypeName = "string"; Kind = Primitive String; Attributes = [] }; Attributes = SerdeAttributes.empty; Capability = Both }
        ]
        UnionCases = None
        EnumCases = None
    }

    let code = CodeEmitter.emit emitter info
    Assert.That(code, Is.EqualTo("FAKE: Person"))

[<Test>]
let ``DebugEmitter emits debug comment`` () =
    let emitter = DebugEmitter() :> ISerdeCodeEmitter
    let info = {
        Raw = {
            Namespace = Some "MyApp"
            EnclosingModules = []
            TypeName = "Person"
            Kind = Record [
                { Name = "FName"; Type = { Namespace = None; EnclosingModules = []; TypeName = "string"; Kind = Primitive String; Attributes = [] }; Attributes = [] }
            ]
            Attributes = []
        }
        Capability = Both
        Attributes = SerdeAttributes.empty
        Fields = Some [
            { Name = "FName"; RawName = "FName"; Type = { Namespace = None; EnclosingModules = []; TypeName = "string"; Kind = Primitive String; Attributes = [] }; Attributes = SerdeAttributes.empty; Capability = Both }
        ]
        UnionCases = None
        EnumCases = None
    }

    let code = emitter.Emit(info)
    Assert.That(code, Is.EqualTo("// DEBUG EMIT: Person"))
