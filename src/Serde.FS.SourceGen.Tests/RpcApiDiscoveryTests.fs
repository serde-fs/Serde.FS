module Serde.FS.SourceGen.Tests.RpcApiDiscoveryTests

open NUnit.Framework
open Serde.FS.SourceGen

/// Regression test for issue #7: types defined in nested modules and referenced
/// with a partial qualifier (e.g., `Auth.User`) must be resolved to a fully
/// qualified name in generated RPC code; previously the partial qualifier was
/// emitted verbatim and didn't resolve in the generated namespace.
[<Test>]
let ``Resolves nested-module type referenced with module qualifier to FQN`` () =
    let entitiesSource = """
namespace MyApp.Domain

type Date = { Year: int; Month: int; Day: int }
"""
    let authSource = """
namespace MyApp.Domain

module Auth =
    type User = { Id: int; Email: string }
"""
    let apiSource = """
module MyApp.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetUser : unit -> Async<Auth.User>
    abstract GetToday : unit -> Async<Date>
"""

    let allTypeInfos =
        [ "/Entities.fs", entitiesSource
          "/Auth.fs", authSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result =
        RpcApiDiscovery.discover allTypeInfos [
            "/Entities.fs", entitiesSource
            "/Auth.fs", authSource
            "/Api.fs", apiSource
        ]

    Assert.That(result.Interfaces.Length, Is.EqualTo(1))
    let iface = result.Interfaces.[0]
    Assert.That(iface.ShortName, Is.EqualTo("IServerApi"))

    let getUser = iface.Methods |> List.find (fun m -> m.MethodName = "GetUser")
    Assert.That(getUser.OutputType, Is.EqualTo("MyApp.Domain.Auth.User"))

    let getToday = iface.Methods |> List.find (fun m -> m.MethodName = "GetToday")
    Assert.That(getToday.OutputType, Is.EqualTo("MyApp.Domain.Date"))

/// Regression test for issue #8: when two modules in the same assembly define
/// types with the same simple name, a partially-qualified reference like
/// `Forge.Project` must resolve to the type whose enclosing path actually ends
/// with `Forge.Project`, not to the later-compiled definition with the same
/// short name.
[<Test>]
let ``Disambiguates same-named types in different modules using partial qualifier`` () =
    let entitiesSource = """
namespace MyApp.Domain

module Forge =
    type Project = { Id: string; HubId: string; Name: string }
"""
    // A second `Project` type, compiled AFTER Forge.Project. Under the old
    // short-name-keyed lookup this would shadow Forge.Project in the resolver.
    let projectModuleSource = """
module MyApp.Domain.Project

type Project = { Id: System.Guid; Name: string }
"""
    let apiSource = """
module MyApp.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetProjects : unit -> Async<Forge.Project list>
"""

    let allTypeInfos =
        [ "/Entities.fs", entitiesSource
          "/Project.fs", projectModuleSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result =
        RpcApiDiscovery.discover allTypeInfos [
            "/Entities.fs", entitiesSource
            "/Project.fs", projectModuleSource
            "/Api.fs", apiSource
        ]

    Assert.That(result.Interfaces.Length, Is.EqualTo(1))
    let iface = result.Interfaces.[0]
    let getProjects = iface.Methods |> List.find (fun m -> m.MethodName = "GetProjects")
    Assert.That(getProjects.OutputType, Is.EqualTo("MyApp.Domain.Forge.Project list"))

/// Regression test for the missing-codec bug surfaced in the CEI.BimHub
/// migration: when an [<RpcApi>] interface references a type via a partial
/// module qualifier (e.g. `Async<Auth.AuthUserResponse>`), discovery must
/// still add that type AND its transitively-referenced types to
/// DiscoveredTypes. Otherwise the Fable emitter generates `XxxCodec.encode`
/// references for codec modules that were never emitted.
[<Test>]
let ``Types referenced via partial qualifier are present in DiscoveredTypes (root + transitive)`` () =
    let authSource = """
namespace MyApp.Domain

module Auth =
    type User = { Email: string }
    type AuthUserResponse = { User: User; Token: string }
"""
    let apiSource = """
module MyApp.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetCurrentUser : unit -> Async<Auth.AuthUserResponse>
"""

    let allTypeInfos =
        [ "/Auth.fs", authSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result =
        RpcApiDiscovery.discover allTypeInfos [
            "/Auth.fs", authSource
            "/Api.fs", apiSource
        ]

    let discovered =
        result.DiscoveredTypes
        |> List.map (fun t -> t.Raw.TypeName)
        |> Set.ofList

    // Root type from the method signature must be discovered…
    Assert.That(discovered, Does.Contain "AuthUserResponse",
        sprintf "AuthUserResponse missing from DiscoveredTypes. Got: %A" discovered)
    // …and so must its transitively-referenced field type.
    Assert.That(discovered, Does.Contain "User",
        sprintf "User (transitive from AuthUserResponse) missing from DiscoveredTypes. Got: %A" discovered)

/// Regression test for the CEI.BimHub case where a Domain record references a
/// type in a sibling module via partial qualifier (`Forge.Hub`), and another
/// module also defines a same-named type (`Hub`). The transitive walk must
/// resolve `Forge.Hub` via suffix lookup — not collapse it onto whichever
/// `Hub` happens to win the short-name lookup.
[<Test>]
let ``Transitive field reference via partial qualifier resolves the right type`` () =
    let forgeSource = """
namespace MyApp.Domain

module Forge =
    type Hub = { Id: string; Name: string }
"""
    // Another `Hub` with the same short name — would shadow Forge.Hub in any
    // short-name-keyed lookup.
    let elsewhereSource = """
namespace MyApp.Other

type Hub = { Code: int }
"""
    let domainSource = """
namespace MyApp.Domain

type ProjectWithHub = { Project: string; Hub: Forge.Hub }
"""
    let apiSource = """
module MyApp.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetProjects : unit -> Async<ProjectWithHub list>
"""

    let allTypeInfos =
        [ "/Forge.fs", forgeSource
          "/Elsewhere.fs", elsewhereSource
          "/Domain.fs", domainSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result =
        RpcApiDiscovery.discover allTypeInfos [
            "/Forge.fs", forgeSource
            "/Elsewhere.fs", elsewhereSource
            "/Domain.fs", domainSource
            "/Api.fs", apiSource
        ]

    let discoveredFqns =
        result.DiscoveredTypes
        |> List.map (fun t ->
            let parts =
                [ yield! t.Raw.Namespace |> Option.toList
                  yield! t.Raw.EnclosingModules
                  yield t.Raw.TypeName ]
            String.concat "." parts)
        |> Set.ofList

    // ProjectWithHub is the root referenced by the interface
    Assert.That(discoveredFqns, Does.Contain "MyApp.Domain.ProjectWithHub",
        sprintf "Got: %A" discoveredFqns)
    // Forge.Hub is the field reference — must be discovered via partial qualifier,
    // not collapsed onto MyApp.Other.Hub
    Assert.That(discoveredFqns, Does.Contain "MyApp.Domain.Forge.Hub",
        sprintf "Got: %A" discoveredFqns)
    // MyApp.Other.Hub is unrelated to the API; should NOT be discovered.
    Assert.That(discoveredFqns, Does.Not.Contain "MyApp.Other.Hub",
        sprintf "Unrelated Hub leaked into DiscoveredTypes. Got: %A" discoveredFqns)

/// Regression: a record with a `Result<T, E>` field used to make the engine
/// emit "Serde error: Type 'Result<...>' ... 'Result' is not marked with
/// [<Serde>]" because Result's definition isn't in user code. Built-in
/// generics (Result, list, option, Map, Set) are now skipped in that
/// validation — they're handled by runtime codec factories.
[<Test>]
let ``Record with Result field doesn't error "Result not marked with Serde"`` () =
    let domainSource = """
namespace MyApp.Domain

type Ok = { Value: int }
type Err = { Message: string }
type Payload = { Result: Result<Ok, Err> }
"""
    let apiSource = """
module MyApp.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetPayload : unit -> Async<Payload>
"""

    let allTypeInfos =
        [ "/Domain.fs", domainSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    // Just running discover with these sources shouldn't throw or surface
    // generic-discovery errors. The Result type must be tolerated.
    let result =
        RpcApiDiscovery.discover allTypeInfos [
            "/Domain.fs", domainSource
            "/Api.fs", apiSource
        ]

    Assert.That(result.Interfaces.Length, Is.EqualTo(1))
    let discoveredFqns =
        result.DiscoveredTypes
        |> List.map (fun t ->
            let parts =
                [ yield! t.Raw.Namespace |> Option.toList
                  yield! t.Raw.EnclosingModules
                  yield t.Raw.TypeName ]
            String.concat "." parts)
        |> Set.ofList
    // Payload and its Ok/Err args must be discovered (Result is a transparent
    // wrapper).
    Assert.That(discoveredFqns, Does.Contain "MyApp.Domain.Payload")
    Assert.That(discoveredFqns, Does.Contain "MyApp.Domain.Ok")
    Assert.That(discoveredFqns, Does.Contain "MyApp.Domain.Err")

/// Regression: a record field whose type is a top-level F# type abbreviation
/// (`type SheetNumber = Guid`) used to trigger "SheetNumber is used in
/// serialization but does not have Serde metadata" because aliases erase at
/// compile time and don't appear in the discovered TypeInfos. Aliases are now
/// added to `serdeTypeNames` so the validator accepts them.
[<Test>]
let ``Type alias used in a field does not error "has no Serde metadata"`` () =
    let domainSource = """
module MyApp.Domain.Api

open System
open Serde.FS

type SheetNumber = string
type TaskId = Guid

type Sheet = { Number: SheetNumber; TaskId: TaskId }

[<RpcApi>]
type IServerApi =
    abstract GetSheet : unit -> Async<Sheet>
"""
    let sourceFiles = [ "/Api.fs", domainSource ]

    // Run the full engine: this is where the NestedTypeValidator fires.
    let emitter = Serde.FS.Json.SourceGen.JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
    let result = Serde.FS.SourceGen.SerdeGeneratorEngine.generate sourceFiles emitter

    Assert.That(result.Errors, Is.Empty, sprintf "Unexpected errors: %A" result.Errors)

/// Regression for the CEI.BimHub generator-output errors. The parser captures
/// field types verbatim:
///   * `Conduit: ConduitSchedule.Conduit`  → TypeInfo with TN="ConduitSchedule.Conduit"
///   * `Number: SheetNumber`               → TypeInfo with TN="SheetNumber" (no real type)
/// Without normalization these flow into the codec emitter as-is and produce
/// `IJsonCodec<ConduitSchedule.Conduit>` / `IJsonCodec<SheetNumber>` which the
/// generated server module can't resolve in its own scope.
///
/// rpcDiscoveryResult.ResolveFieldType must:
///   * Expand `SheetNumber` to the alias target (Primitive String).
///   * Resolve `ConduitSchedule.Conduit` to its canonical FQN parts
///     (Namespace + EnclosingModules + TypeName).
[<Test>]
let ``ResolveFieldType normalises partial qualifiers and expands aliases`` () =
    let domainSource = """
namespace CEI.BimHub.Domain

module ConduitSchedule =
    type Conduit = { Id: int }

module FeederRelease =
    type Conduit = { Id: int }
"""
    let apiSource = """
module CEI.BimHub.Domain.Api

open Serde.FS

type SheetNumber = string

type Foo = { Number: SheetNumber; Conduit: ConduitSchedule.Conduit }

[<RpcApi>]
type IServerApi =
    abstract GetFoo : unit -> Async<Foo>
"""
    let allTypeInfos =
        [ "/Domain.fs", domainSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result = RpcApiDiscovery.discover allTypeInfos [
        "/Domain.fs", domainSource
        "/Api.fs", apiSource
    ]

    // Locate Foo in allTypeInfos and pull its field TypeInfos as the parser
    // captured them, then run them through ResolveFieldType.
    let foo =
        allTypeInfos
        |> List.find (fun ti -> ti.TypeName = "Foo")
    let fields =
        match foo.Kind with
        | FSharp.SourceDjinn.TypeModel.Types.Record fs -> fs
        | _ -> failwith "expected Record"

    let numberField = fields |> List.find (fun f -> f.Name = "Number")
    let conduitField = fields |> List.find (fun f -> f.Name = "Conduit")

    let resolvedNumber = result.ResolveFieldType [] numberField.Type
    let resolvedConduit = result.ResolveFieldType [] conduitField.Type

    // SheetNumber alias expanded to Primitive String.
    match resolvedNumber.Kind with
    | FSharp.SourceDjinn.TypeModel.Types.Primitive FSharp.SourceDjinn.TypeModel.Types.PrimitiveKind.String -> ()
    | other -> Assert.Fail (sprintf "expected Primitive String, got %A" other)

    // ConduitSchedule.Conduit normalised to canonical FQN parts.
    Assert.That(resolvedConduit.Namespace, Is.EqualTo(Some "CEI.BimHub.Domain"),
        sprintf "Namespace mismatch — got %A" resolvedConduit.Namespace)
    Assert.That(resolvedConduit.EnclosingModules, Is.EqualTo([ "ConduitSchedule" ]),
        sprintf "EnclosingModules mismatch — got %A" resolvedConduit.EnclosingModules)
    Assert.That(resolvedConduit.TypeName, Is.EqualTo("Conduit"),
        sprintf "TypeName mismatch — got %s" resolvedConduit.TypeName)

/// Regression for the CEI.BimHub ambiguous-short-name case. When two modules
/// declare a type with the same simple name (`ConduitSchedule.Conduit` and
/// `FeederRelease.Conduit`), an unqualified field reference inside one of the
/// owning modules must resolve to the SAME-module type, mirroring F#'s lexical
/// scoping. Without parentScope-aware resolution the suffix lookup picks an
/// arbitrary candidate, surfacing as a type-mismatch in generated server code.
[<Test>]
let ``ResolveFieldType prefers same-module type for ambiguous short names`` () =
    let domainSource = """
namespace CEI.BimHub.Domain

module ConduitSchedule =
    type Conduit = { ScheduleId: int }
    type ConduitInstance = { Conduit: Conduit }

module FeederRelease =
    type Conduit = { ReleaseId: int }
    type FeederConduit = { Conduit: Conduit }
"""
    let apiSource = """
module CEI.BimHub.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetSchedule : unit -> Async<ConduitSchedule.ConduitInstance>
    abstract GetFeeder : unit -> Async<FeederRelease.FeederConduit>
"""
    let allTypeInfos =
        [ "/Domain.fs", domainSource
          "/Api.fs", apiSource ]
        |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result = RpcApiDiscovery.discover allTypeInfos [
        "/Domain.fs", domainSource
        "/Api.fs", apiSource
    ]

    let conduitFieldOf typeName =
        let ti =
            allTypeInfos
            |> List.find (fun ti ->
                ti.TypeName = typeName
                && ti.Namespace = Some "CEI.BimHub.Domain")
        match ti.Kind with
        | FSharp.SourceDjinn.TypeModel.Types.Record fs ->
            fs |> List.find (fun f -> f.Name = "Conduit")
        | _ -> failwith "expected Record"

    // ConduitInstance lives in ConduitSchedule → its unqualified `Conduit`
    // field must resolve to ConduitSchedule.Conduit.
    let scheduleScope = [ "CEI"; "BimHub"; "Domain"; "ConduitSchedule" ]
    let scheduleConduit = result.ResolveFieldType scheduleScope (conduitFieldOf "ConduitInstance").Type
    Assert.That(scheduleConduit.EnclosingModules, Is.EqualTo([ "ConduitSchedule" ]),
        sprintf "ConduitInstance.Conduit should resolve to ConduitSchedule.Conduit, got %A.%s"
            scheduleConduit.EnclosingModules scheduleConduit.TypeName)

    // FeederConduit lives in FeederRelease → its unqualified `Conduit` field
    // must resolve to FeederRelease.Conduit.
    let feederScope = [ "CEI"; "BimHub"; "Domain"; "FeederRelease" ]
    let feederConduit = result.ResolveFieldType feederScope (conduitFieldOf "FeederConduit").Type
    Assert.That(feederConduit.EnclosingModules, Is.EqualTo([ "FeederRelease" ]),
        sprintf "FeederConduit.Conduit should resolve to FeederRelease.Conduit, got %A.%s"
            feederConduit.EnclosingModules feederConduit.TypeName)

/// Regression for the CEI.BimHub union-case-with-partial-qualifier bug.
/// SourceDjinn captures `ProjectSelected of Forge.Project` as a union case
/// field with TypeName="Forge.Project" and empty EnclosingModules. When two
/// `Project` types exist (one in `Forge`, another in `Project` module), the
/// resolver's scope-walk used to build synthetic candidates like
/// `CEI.BimHub.Domain.DrawingLog.Forge.Project` and pass them through the
/// forgiving resolver — which short-name-salvaged to `Project` (ambiguous) and
/// returned the WRONG candidate. Scope-walk candidates must now go through
/// the STRICT resolver (no short-name fallback), so synthetic names that
/// don't uniquely match are rejected and the fallback uses the user-written
/// partial-qualifier `Forge.Project` for resolution.
[<Test>]
let ``ResolveFieldType uses strict scope-walk for union case partial qualifier`` () =
    let source = """
namespace CEI.BimHub.Domain

module Forge =
    [<Serde.FS.Serde>]
    type Project = { Id: int }

module Project =
    [<Serde.FS.Serde>]
    type Project = { Code: string }

module DrawingLog =
    [<Serde.FS.Serde>]
    type ProjectSelectionState =
        | NotAuthorizedForProject
        | ProjectSelected of Forge.Project
"""
    let allTypeInfos = SerdeAstParser.parseSourceAllTypes "/test.fs" source
    let result = RpcApiDiscovery.discover allTypeInfos [ "/test.fs", source ]

    // Find ProjectSelectionState's `ProjectSelected` field and resolve it.
    let pss =
        allTypeInfos
        |> List.find (fun ti -> ti.TypeName = "ProjectSelectionState")
    let projectSelectedFieldTi =
        match pss.Kind with
        | FSharp.SourceDjinn.TypeModel.Types.Union cases ->
            let c = cases |> List.find (fun c -> c.CaseName = "ProjectSelected")
            c.Fields.[0].Type
        | _ -> failwith "expected Union"

    // The parent scope of ProjectSelectionState's fields is the union's own scope.
    let parentScope = [ "CEI"; "BimHub"; "Domain"; "DrawingLog" ]
    let resolved = result.ResolveFieldType parentScope projectSelectedFieldTi

    Assert.That(resolved.Namespace, Is.EqualTo(Some "CEI.BimHub.Domain"),
        sprintf "Namespace mismatch — got %A" resolved.Namespace)
    Assert.That(resolved.EnclosingModules, Is.EqualTo([ "Forge" ]),
        sprintf "Should resolve to Forge.Project not Project.Project — got %A.%s"
            resolved.EnclosingModules resolved.TypeName)
    Assert.That(resolved.TypeName, Is.EqualTo("Project"))

/// Regression for the second batch of CEI.BimHub validator errors:
/// transitive discovery used to walk fields via short-name lookup so when
/// two modules each declared a type with the same simple name and both were
/// referenced (one from each owning module), only one got discovered. The
/// validator then rejected the other. Discovery now walks TypeInfo directly
/// with parent-scope resolution so BOTH candidates get pulled in.
[<Test>]
let ``Transitive discovery includes both same-short-name types when both are referenced`` () =
    let domainSource = """
namespace CEI.BimHub.Domain

[<Serde.FS.Serde>]
type ScheduleLookup = { ScheduleId: int }

[<Serde.FS.Serde>]
type ReleaseLookup = { ReleaseId: int }
"""

    let scheduleSource = """
namespace CEI.BimHub.Domain

module ConduitSchedule =
    [<Serde.FS.Serde>]
    type FeederTagWireLookup = { Schedule: ScheduleLookup }

    [<Serde.FS.Serde>]
    type Conduit = { Lookup: FeederTagWireLookup }
"""

    let releaseSource = """
namespace CEI.BimHub.Domain

module FeederRelease =
    [<Serde.FS.Serde>]
    type FeederTagWireLookup = { Release: ReleaseLookup }

    [<Serde.FS.Serde>]
    type Conduit = { Lookup: FeederTagWireLookup }
"""

    let apiSource = """
module CEI.BimHub.Domain.Api

open Serde.FS

[<RpcApi>]
type IServerApi =
    abstract GetSchedule : unit -> Async<ConduitSchedule.Conduit>
    abstract GetRelease : unit -> Async<FeederRelease.Conduit>
"""
    let sources = [
        "/Domain.fs", domainSource
        "/Schedule.fs", scheduleSource
        "/Release.fs", releaseSource
        "/Api.fs", apiSource
    ]
    let allTypeInfos =
        sources |> List.collect (fun (path, src) -> SerdeAstParser.parseSourceAllTypes path src)

    let result = RpcApiDiscovery.discover allTypeInfos sources

    let fqn (sti: Serde.FS.SerdeTypeInfo) =
        let raw = sti.Raw
        [ yield! raw.Namespace |> Option.toList
          yield! raw.EnclosingModules
          yield raw.TypeName ]
        |> String.concat "."

    let discoveredFqns = result.DiscoveredTypes |> List.map fqn |> Set.ofList

    Assert.That(discoveredFqns.Contains "CEI.BimHub.Domain.ConduitSchedule.FeederTagWireLookup", Is.True,
        sprintf "ConduitSchedule.FeederTagWireLookup missing — discovered: %A" discoveredFqns)
    Assert.That(discoveredFqns.Contains "CEI.BimHub.Domain.FeederRelease.FeederTagWireLookup", Is.True,
        sprintf "FeederRelease.FeederTagWireLookup missing — discovered: %A" discoveredFqns)
    Assert.That(discoveredFqns.Contains "CEI.BimHub.Domain.ConduitSchedule.Conduit", Is.True,
        sprintf "ConduitSchedule.Conduit missing — discovered: %A" discoveredFqns)
    Assert.That(discoveredFqns.Contains "CEI.BimHub.Domain.FeederRelease.Conduit", Is.True,
        sprintf "FeederRelease.Conduit missing — discovered: %A" discoveredFqns)

/// Issue #13: the generated entry point must run the bootstraps of its own
/// compilation via direct, statically-rooted calls (Native AOT / trimming
/// safe) before falling back to the reflection scan for referenced
/// assemblies.
[<Test>]
let ``Generated entry point runs local bootstraps directly before the scan`` () =
    let source = """
module MyApp.Program

open Serde.FS

[<Serde>]
type Person = { Name: string }

[<RpcApi>]
type IPersonApi =
    abstract GetPerson : unit -> Async<Person>

[<Serde.FS.EntryPoint>]
let main argv = 0
"""
    // The engine only emits the wrapper for sources under the project dir
    // (= current directory in tests).
    let path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Program.fs")
    let emitter = Serde.FS.Json.SourceGen.JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
    let result = Serde.FS.SourceGen.SerdeGeneratorEngine.generate [ path, source ] emitter

    Assert.That(result.Errors, Is.Empty, sprintf "Unexpected errors: %A" result.Errors)
    let entryPoint =
        result.Sources |> List.tryFind (fun s -> s.HintName = "~~EntryPoint.djinn.g.fs")
    Assert.That(entryPoint.IsSome, Is.True, "entry point wrapper should be generated")

    let code = entryPoint.Value.Code
    Assert.That(code, Does.Contain("Serde.FS.Bootstrap.Run([|"))
    Assert.That(code, Does.Contain("Serde.Generated.JsonBootstrap() :> Serde.FS.IEntryPointBootstrap"))
    Assert.That(code, Does.Contain("SerdeGenerated.Rpc.IPersonApiRpcBootstrap() :> Serde.FS.IEntryPointBootstrap"))
    // The parameterless scan still follows the direct calls.
    Assert.That(code, Does.Contain("Serde.FS.Bootstrap.Run()"))

/// The RPC module must carry the assembly-level SerdeBootstrap marker so
/// Bootstrap.Run(assembly) finds the bootstrap without a type scan.
[<Test>]
let ``RPC module emits assembly-level SerdeBootstrap attribute`` () =
    let source = """
module MyApp.Program

open Serde.FS

[<Serde>]
type Person = { Name: string }

[<RpcApi>]
type IPersonApi =
    abstract GetPerson : unit -> Async<Person>
"""
    let emitter = Serde.FS.Json.SourceGen.JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
    let result = Serde.FS.SourceGen.SerdeGeneratorEngine.generate [ "/Program.fs", source ] emitter

    Assert.That(result.Errors, Is.Empty, sprintf "Unexpected errors: %A" result.Errors)
    let rpcModule =
        result.Sources |> List.tryFind (fun s -> s.HintName = "~Rpc.IPersonApi.json.g.fs")
    Assert.That(rpcModule.IsSome, Is.True, "RPC module should be generated")
    Assert.That(rpcModule.Value.Code,
        Does.Contain("[<assembly: Serde.FS.SerdeBootstrap(typeof<SerdeGenerated.Rpc.IPersonApiRpcBootstrap>)>]"))
