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
