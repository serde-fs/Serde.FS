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
