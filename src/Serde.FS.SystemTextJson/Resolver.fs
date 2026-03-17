namespace Serde.FS.SystemTextJson

open System.Text.Json
open System.Text.Json.Serialization.Metadata

/// Collects IJsonTypeInfoResolver instances registered by IBootstrap implementations at startup.
[<RequireQualifiedAccess>]
module Resolver =

    let private resolvers = ResizeArray<IJsonTypeInfoResolver>()

    let register (r: IJsonTypeInfoResolver) =
        resolvers.Add(r)

    let get () =
        let builtIn = DefaultJsonTypeInfoResolver() :> IJsonTypeInfoResolver
        let generated = JsonTypeInfoResolver.Combine(resolvers.ToArray())
        JsonTypeInfoResolver.Combine(generated, builtIn)

    /// Creates a JsonSerializerOptions instance using the given defaults, 
    /// attaches the full resolver chain, and returns the configured options.
    let createOptions (defaults : JsonSerializerDefaults) =
        let options = JsonSerializerOptions(defaults)
        options.TypeInfoResolver <- get ()
        options
