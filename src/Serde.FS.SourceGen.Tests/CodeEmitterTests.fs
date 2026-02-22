module Serde.FS.SourceGen.Tests.CodeEmitterTests

open NUnit.Framework
open Serde.FS
open Serde.FS.SourceGen

type FakeEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) = sprintf "FAKE: %s" info.TypeName

[<Test>]
let ``CodeEmitter delegates to ISerdeCodeEmitter`` () =
    let emitter = FakeEmitter() :> ISerdeCodeEmitter
    let info = {
        Namespace = Some "MyApp"
        EnclosingModules = []
        TypeName = "Person"
        Capability = Both
        Fields = [
            { Name = "FName"; FSharpType = "string" }
        ]
    }

    let code = CodeEmitter.emit emitter info
    Assert.That(code, Is.EqualTo("FAKE: Person"))

[<Test>]
let ``DebugEmitter emits debug comment`` () =
    let emitter = DebugEmitter() :> ISerdeCodeEmitter
    let info = {
        Namespace = Some "MyApp"
        EnclosingModules = []
        TypeName = "Person"
        Capability = Both
        Fields = [
            { Name = "FName"; FSharpType = "string" }
        ]
    }

    let code = emitter.Emit(info)
    Assert.That(code, Is.EqualTo("// DEBUG EMIT: Person"))
