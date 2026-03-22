module Serde.FS.Json.RpcClient

open System
open System.Collections.Concurrent
open System.Net.Http

/// Registry of RPC client factories, populated by generated code.
let private factories = ConcurrentDictionary<Type, string -> HttpClient -> obj>()

/// Register a client factory for an RPC interface. Called by generated code.
let register<'TApi> (factory: string -> HttpClient -> obj) =
    factories.[typeof<'TApi>] <- factory

/// Create a typed RPC client proxy for the given interface.
/// The proxy serializes calls via SerdeJson and sends them as HTTP POST requests.
let Create<'TApi> (baseUrl: string) (http: HttpClient) : 'TApi =
    match factories.TryGetValue(typeof<'TApi>) with
    | true, f -> f baseUrl http :?> 'TApi
    | _ ->
        invalidOp $"No RPC client registered for type '%s{typeof<'TApi>.FullName}'. Ensure the project references Serde.FS.Json and the [<RpcApi>] interface is visible to the source generator."
