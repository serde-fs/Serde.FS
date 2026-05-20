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
let ``Probe: end-to-end engine vs CEI.BimHub-shaped sources`` () =
    // Reproduce the user's structural pattern:
    //   - A `module Forge` file (top-level module, no enclosing namespace)
    //     defining Hub / Project.
    //   - A Domain file under `module CEI.BimHub.Domain.X` defining a type
    //     with fields that reference Forge types via partial qualifier.
    //   - An interface in `module CEI.BimHub.Domain.Api` returning the
    //     domain type.
    // Run the FULL engine and see whether validation complains. This mirrors
    // the CEI.BimHub failure mode where 'Forge.Hub does not have Serde
    // metadata' fires even though Forge.Hub is, in principle, discoverable.
    // Mimic the CEI.BimHub real-world shape: Hub/Project live deeper than
    // just "Forge" (e.g. namespace CEI.BimHub.Forge). The Domain field
    // references them via partial qualifier "Forge.Hub", which the parser
    // captures as TypeName="Forge.Hub" with empty namespace info — different
    // from the actual declaration's FQN "CEI.BimHub.Forge.Hub".
    let forgeSource = """
namespace CEI.BimHub.Forge

type Hub = { Id: string; Name: string }
type Project = { Id: string; HubId: string }
"""
    let domainSource = """
module CEI.BimHub.Domain.ConduitSchedule

type ConcurrencyErrorDetails = { Message: string }
type ProjectWithHub = { Hub: Forge.Hub; Project: Forge.Project; Err: ConcurrencyErrorDetails }
"""
    let apiSource = """
module CEI.BimHub.Domain.Api

open Serde.FS
open CEI.BimHub.Forge  // so Forge.Hub references resolve

[<RpcApi>]
type IServerApi =
    abstract GetProjects : unit -> Async<CEI.BimHub.Domain.ConduitSchedule.ProjectWithHub>
"""
    let sourceFiles = [
        "/Forge.fs", forgeSource
        "/ConduitSchedule.fs", domainSource
        "/Api.fs", apiSource
    ]
    let emitter = Serde.FS.Json.SourceGen.JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
    let result = Serde.FS.SourceGen.SerdeGeneratorEngine.generate sourceFiles emitter

    printfn "=== Errors ==="
    for e in result.Errors do printfn "  %s" e
    printfn "=== Warnings ==="
    for w in result.Warnings do printfn "  %s" w
    printfn "=== Sources ==="
    for s in result.Sources do printfn "  %s (abs=%A)" s.HintName s.AbsolutePath

    Assert.That(result.Errors, Is.Empty, sprintf "Unexpected errors: %A" result.Errors)

[<Test>]
let ``Probe: dump field TypeInfo for partial qualifier`` () =
    // Diagnostic probe — not a real assertion. Just dumps what SerdeAstParser
    // produces for a field referenced via partial qualifier (Forge.Hub) so
    // we can see what the FieldTypeResolver / NestedTypeValidator are seeing.
    let forgeSource = """
module Forge

type Hub = { Id: int }
"""
    let domainSource = """
module MyApp.Domain

type ProjectWithHub = { Hub: Forge.Hub }
"""
    let forgeTis = Serde.FS.SourceGen.SerdeAstParser.parseSourceAllTypes "/Forge.fs" forgeSource
    let domainTis = Serde.FS.SourceGen.SerdeAstParser.parseSourceAllTypes "/Domain.fs" domainSource

    printfn "=== Forge ==="
    for t in forgeTis do
        printfn "  type: NS=%A EM=%A TN=%s" t.Namespace t.EnclosingModules t.TypeName

    printfn "=== Domain ==="
    for t in domainTis do
        printfn "  type: NS=%A EM=%A TN=%s" t.Namespace t.EnclosingModules t.TypeName
        match t.Kind with
        | FSharp.SourceDjinn.TypeModel.Types.Record fields ->
            for f in fields do
                printfn "    field %s: NS=%A EM=%A TN=%s Kind=%A"
                    f.Name f.Type.Namespace f.Type.EnclosingModules f.Type.TypeName f.Type.Kind
        | _ -> ()
    Assert.Pass()

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

[<Test>]
let ``record with option field`` () =
    let userTi =
        record "Domain" "User" [
            "Id",    int32Ti
            "Email", opt stringTi
        ]
    let methods = [
        methodOf "GetUser" int32Ti userTi
    ]
    let iface = interfaceOf "Domain" "IUserApi" methods true
    let types = [ toSerde userTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "record_option_field" actual

[<Test>]
let ``record with list field`` () =
    let cartTi =
        record "Domain" "Cart" [
            "Id",    int32Ti
            "Items", listTi stringTi
        ]
    let methods = [
        nullaryMethod "GetCart" cartTi
    ]
    let iface = interfaceOf "Domain" "ICartApi" methods true
    let types = [ toSerde cartTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "record_list_field" actual

[<Test>]
let ``record referencing another record across namespaces`` () =
    // Domain.Catalog.Product is referenced from Domain.Shop.Order — the
    // emitter must produce a fully-qualified annotation for the cross-namespace
    // field and reference the *short* codec name (ProductCodec, not
    // Domain_Catalog_ProductCodec).
    let productTi =
        record "Domain.Catalog" "Product" [
            "Id",    int32Ti
            "Name",  stringTi
            "Price", decimalTi
        ]
    let orderTi =
        record "Domain.Shop" "Order" [
            "OrderId", int32Ti
            "Item",    productTi
        ]
    let methods = [
        methodOf "PlaceOrder" orderTi int32Ti
    ]
    let iface = interfaceOf "Domain.Shop" "IOrderApi" methods true
    let types = [ toSerde productTi; toSerde orderTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "cross_namespace_record" actual

[<Test>]
let ``single-case wrapper union (ProductId of int)`` () =
    let productIdTi = wrapperUnion "Domain" "ProductId" "ProductId" int32Ti
    let methods = [
        methodOf "Lookup" productIdTi stringTi
    ]
    let iface = interfaceOf "Domain" "IProductApi" methods true
    let types = [ toSerde productIdTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "wrapper_union" actual

[<Test>]
let ``enum with three cases`` () =
    let statusTi = enumTi "Domain" "Status" [ "Pending"; "Active"; "Closed" ]
    let methods = [
        nullaryMethod "GetStatus" statusTi
    ]
    let iface = interfaceOf "Domain" "IStatusApi" methods true
    let types = [ toSerde statusTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "enum_three_cases" actual

/// Regression for the CEI.BimHub Fable client collision case. When two types
/// share a short TypeName but live in different enclosing modules (e.g.
/// `Forge.Project` and `Project.Project`), the Fable emitter used to produce
/// two `module private ProjectCodec` declarations in the same generated file
/// AND every `FUser` reference (in record-field encode/decode expressions)
/// would name `ProjectCodec`, so types crossed wires and the F# compiler
/// reported `'Forge.Project' does not match 'Project.Project'`. The emitter
/// must now disambiguate colliding short names via EnclosingModules prefix
/// (`Forge_ProjectCodec`, `Project_ProjectCodec`).
[<Test>]
let ``Fable client disambiguates colliding short-name codec modules`` () =
    let forgeProject =
        nestedRecord "CEI.Domain" [ "Forge" ] "Project" [
            "Id", int32Ti
        ]
    let projectProject =
        nestedRecord "CEI.Domain" [ "Project" ] "Project" [
            "Code", stringTi
        ]
    // An outer record with two fields, each pointing to a different `Project`.
    // The emitter's encode/decode for each field must reference the right
    // codec; the codec modules themselves must have unique names.
    let conflictTi =
        record "CEI.Domain" "ProjectPair" [
            "ForgeP", forgeProject
            "DomainP", projectProject
        ]
    let methods = [ nullaryMethod "GetPair" conflictTi ]
    let iface = interfaceOf "CEI.Domain" "IPairApi" methods true
    let types = [ toSerde forgeProject; toSerde projectProject; toSerde conflictTi ]
    let actual = FableClientEmitter.emit iface types

    // Both disambiguated codec modules must be declared, and the bare
    // `module private ProjectCodec` (which would collide with itself) must NOT
    // appear.
    Assert.That(actual, Does.Contain("module private Forge_ProjectCodec ="),
        "Forge_ProjectCodec module missing")
    Assert.That(actual, Does.Contain("module private Project_ProjectCodec ="),
        "Project_ProjectCodec module missing")
    Assert.That(actual, Does.Not.Contain("module private ProjectCodec ="),
        "bare ProjectCodec would cause a duplicate-module error")

    // The encode/decode expressions for the outer record's fields must
    // reference the disambiguated codec, NOT the bare ProjectCodec.
    Assert.That(actual, Does.Contain("Forge_ProjectCodec.encode"),
        "ForgeP field should encode via Forge_ProjectCodec")
    Assert.That(actual, Does.Contain("Forge_ProjectCodec.decode"),
        "ForgeP field should decode via Forge_ProjectCodec")
    Assert.That(actual, Does.Contain("Project_ProjectCodec.encode"),
        "DomainP field should encode via Project_ProjectCodec")
    Assert.That(actual, Does.Contain("Project_ProjectCodec.decode"),
        "DomainP field should decode via Project_ProjectCodec")

[<Test>]
let ``method returning Result of T, string`` () =
    let productTi =
        record "Domain" "Product" [
            "Id",   int32Ti
            "Name", stringTi
        ]
    let methods = [
        methodOf "TryGetProduct" int32Ti (resultTi productTi stringTi)
    ]
    let iface = interfaceOf "Domain" "IOrderApi" methods true
    let types = [ toSerde productTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "result_return" actual

[<Test>]
let ``record with seq field decodes to seq, not list`` () =
    // Regression: previously synTypeToTypeInfo mapped `seq<T>` to TypeKind.List
    // with TypeName="list", so the Fable decoder produced `string list` even
    // when the field type was `string seq`. F# rejected the assignment.
    // Now TypeName="seq" reaches the emitter, which routes via FSeq and emits
    // `Array.map ... :> seq<_>` so the decoded value is a seq.
    let cacheTi =
        record "Domain" "Cache" [
            "Tags", seqTi stringTi
        ]
    let methods = [
        nullaryMethod "GetCache" cacheTi
    ]
    let iface = interfaceOf "Domain" "ICacheApi" methods true
    let types = [ toSerde cacheTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "record_seq_field" actual

[<Test>]
let ``method with tupled input (A * B -> C)`` () =
    // F# treats `abstract Foo : A * B -> C` as a multi-arg method. The
    // generated proxy member must declare each param individually
    // (`p0: int, p1: int`) and JSON-encode them as a tuple array.
    let productTi =
        record "Domain" "Product" [
            "Id",   int32Ti
            "Name", stringTi
        ]
    let methods = [
        tupledMethod "ListProductsPage" [ int32Ti; int32Ti ] (listTi productTi)
    ]
    let iface = interfaceOf "Domain" "IOrderApi" methods true
    let types = [ toSerde productTi ]
    let actual = FableClientEmitter.emit iface types
    SnapshotHarness.assertSnapshot "tupled_input" actual
