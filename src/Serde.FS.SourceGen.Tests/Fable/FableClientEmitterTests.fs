/// Snapshot tests for FableClientEmitter. Each test builds synthetic
/// TypeInfo + RpcInterfaceInfo inputs, runs the emitter, and asserts the
/// output matches a checked-in `.expected.fs` snapshot. See SnapshotHarness.fs
/// for the comparison + .actual.fs writeback flow.
///
/// These tests pin the *current* emitter output. The intent is that the
/// step-4 refactor (route everything through TypeInfo, delete parseTypeString)
/// produces byte-identical output, with these tests catching any drift.
module Serde.FS.SourceGen.Tests.Fable.FableClientEmitterTests

open NUnit.Framework
open Serde.FS
open Serde.FS.Json.SourceGen
open Serde.FS.SourceGen.Tests.Fable
open Serde.FS.SourceGen.Tests.Fable.SyntheticTypes

[<Test>]
let ``record with primitive fields in a namespace`` () =
    let productTi =
        record "Domain" "Product" [
            "Id", int32Ti
            "Name", stringTi
        ]
    let methods = [
        methodOf "GetProduct" int32Ti productTi
    ]
    let iface = interfaceOf "Domain" "IOrderApi" methods true
    let types = [ toSerde productTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "record_primitives_namespace" actual

[<Test>]
let ``record with primitive fields under a top-level module`` () =
    let productTi =
        record "Domain.Api" "Product" [
            "Id", int32Ti
            "Name", stringTi
        ]
    let methods = [
        methodOf "GetProduct" int32Ti productTi
    ]
    // IsParentNamespace = false → emitter must use the sibling-module shape:
    //   module rec Domain.IOrderApiFableClient
    let iface = interfaceOf "Domain.Api" "IOrderApi" methods false
    let types = [ toSerde productTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "record_primitives_module" actual

[<Test>]
let ``unresolved type yields SerdeFS102 and skips file emission`` () =
    // Build an interface whose output TypeInfo is None — simulating discovery
    // failing to resolve a type. JsonCodeEmitter.EmitCrossProjectFiles must
    // produce an MSBuild-format diagnostic and emit no file.
    let brokenMethod =
        { MethodName = "GetMystery"
          InputType = "int"
          InputIsTupled = false
          InputParams = []
          OutputType = "Mystery.Type.That.Does.Not.Resolve"
          InputTypeInfo = Some int32Ti
          OutputTypeInfo = None
          InputParamTypeInfos = [] }

    let iface =
        { (interfaceOf "Domain" "IBrokenApi" [ brokenMethod ] true) with
            SourceFilePath = Some "/tmp/Domain/Api.fs" }

    let emitter = JsonCodeEmitter() :> ISerdeRpcEmitter
    let result = emitter.EmitCrossProjectFiles([ iface ], [])

    Assert.That(result.Files, Is.Empty, "no file should be emitted when a method's TypeInfo is None")
    Assert.That(result.Errors.Length, Is.EqualTo(1))
    let err = result.Errors.[0]
    Assert.That(err, Does.Contain("error SerdeFS102"))
    Assert.That(err, Does.Contain("'Domain.IBrokenApi'"))
    Assert.That(err, Does.Contain("GetMystery output"))
    Assert.That(err, Does.StartWith("/tmp/Domain/Api.fs(1,1):"))

[<Test>]
let ``multi-case union with mixed cases`` () =
    let shapeTi =
        multiUnion "Domain" "Shape" [
            "Circle",    [ floatTi ]
            "Rectangle", [ floatTi; floatTi ]
            "Point",     [ ]
        ]
    let methods = [
        methodOf "GetShape" int32Ti shapeTi
    ]
    let iface = interfaceOf "Domain" "IShapeApi" methods true
    let types = [ toSerde shapeTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "multi_case_union" actual
