namespace Serde.FS.Json

open System.Text.Json
open System.Text.Json.Serialization.Metadata
open Serde.FS

/// Internal debug logging for the JSON backend.
module internal SerdeDebugLog =
    let log (msg: string) =
        if Serde.Debug then printfn "[SerdeDebug] %s" msg

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
        SerdeDebugLog.log "Attaching generated JSON resolver to JsonSerializerOptions"
        JsonOptionsCache.generatedResolvers.Add(resolver)
        JsonOptionsCache.defaultJsonOptions.TypeInfoResolverChain.Insert(0, resolver)

/// Strict mode enforcement using generated resolvers (AOT-safe, no reflection).
module internal JsonStrict =
    let enforceStrict (ty: System.Type) =
        if Serde.Strict then
            let opts = JsonOptionsCache.defaultJsonOptions
            let found =
                JsonOptionsCache.generatedResolvers
                |> Seq.exists (fun resolver ->
                    resolver.GetTypeInfo(ty, opts) <> null
                )
            if Serde.Debug then
                if found then SerdeDebugLog.log $"Strict mode: found generated metadata for {ty.FullName}"
                else SerdeDebugLog.log $"Strict mode: NO metadata found for {ty.FullName}"
            if not found then
                failwithf
                    "Strict mode is enabled: type '%s' has no generated Serde metadata. \
                     Call SerdeJson.allowReflectionFallback() to allow reflection-based serialization."
                    ty.FullName

type JsonBackend() =
    interface ISerdeBackend with
        member _.Serialize(value, runtimeType, _options) =
            JsonStrict.enforceStrict(runtimeType)
            JsonSerializer.Serialize(value, runtimeType, JsonOptionsCache.defaultJsonOptions)

        member _.Deserialize(json, runtimeType, _options) =
            JsonStrict.enforceStrict(runtimeType)
            JsonSerializer.Deserialize(json, runtimeType, JsonOptionsCache.defaultJsonOptions) :?> 'T
