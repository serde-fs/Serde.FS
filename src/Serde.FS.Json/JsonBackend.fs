namespace Serde.FS.Json

open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Serde.FS

/// Cached JsonSerializerOptions instance used by the JSON backend.
module internal JsonOptionsCache =
    let defaultJsonOptions =
        let opts = JsonSerializerOptions()
        opts.TypeInfoResolverChain.Add(DefaultJsonTypeInfoResolver())
        opts
    let generatedResolvers = System.Collections.Generic.List<IJsonTypeInfoResolver>()

/// Registry for generated IJsonTypeInfoResolvers.
/// Generated code calls registerResolver at module init to wire resolvers into the JSON backend.
module SerdeJsonResolverRegistry =
    let registerResolver (resolver: IJsonTypeInfoResolver) =
        JsonOptionsCache.generatedResolvers.Add(resolver)
        JsonOptionsCache.defaultJsonOptions.TypeInfoResolverChain.Insert(0, resolver)

/// Strict enforcement using generated resolvers (AOT-safe, no reflection).
/// Throws if a type has no generated Serde metadata.
module internal JsonStrict =
    let enforceStrict (ty: System.Type) =
        let opts = JsonOptionsCache.defaultJsonOptions
        let found =
            JsonOptionsCache.generatedResolvers
            |> Seq.exists (fun resolver ->
                resolver.GetTypeInfo(ty, opts) <> null
            )
        if not found then
            failwithf
                "Type '%s' has no generated Serde metadata. Mark it with [<Serde>] to enable serialization."
                ty.FullName

type JsonBackend() =
    interface ISerdeBackend with
        member _.Serialize(value, runtimeType, _options) =
            JsonStrict.enforceStrict(runtimeType)
            JsonSerializer.Serialize(value, runtimeType, JsonOptionsCache.defaultJsonOptions)

        member _.Deserialize(json, runtimeType, _options) =
            JsonStrict.enforceStrict(runtimeType)
            JsonSerializer.Deserialize(json, runtimeType, JsonOptionsCache.defaultJsonOptions) :?> 'T
