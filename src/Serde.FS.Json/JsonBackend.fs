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

type JsonBackend() =
    interface ISerdeBackend with
        member _.Serialize(value, runtimeType, _options) =
            SerdeMetadata.get runtimeType |> ignore
            JsonSerializer.Serialize(value, runtimeType, JsonOptionsCache.defaultJsonOptions)

        member _.Deserialize(json, runtimeType, _options) =
            // Spec 36: Detect missing generic type argument for wrapper DUs
            let isTooGeneric =
                runtimeType = typeof<obj>
                || not runtimeType.IsGenericType
                || runtimeType.IsGenericTypeDefinition
                || runtimeType.GetGenericArguments().Length = 0

            if isTooGeneric then
                try
                    let doc = JsonDocument.Parse(json)
                    let root = doc.RootElement
                    if root.ValueKind = JsonValueKind.Object then
                        let mutable count = 0
                        let mutable caseName = ""
                        for prop in root.EnumerateObject() do
                            count <- count + 1
                            if count = 1 then caseName <- prop.Name
                        if count = 1 then
                            match SerdeMetadata.tryFindGenericWrapperByCaseName caseName with
                            | Some wrapperName ->
                                let msg =
                                    "Serde.FS: Cannot deserialize a generic wrapper type " +
                                    "without specifying the closed generic type.\n\n" +
                                    $"The JSON represents a value of type '{wrapperName}<_>'.\n" +
                                    $"You must call Deserialize<{wrapperName}<ConcreteType>> to deserialize this value."
                                raise (SerdeMissingMetadataException(msg, runtimeType))
                            | None -> ()
                with
                | :? SerdeMissingMetadataException -> reraise()
                | _ -> () // JSON parse failed or not wrapper-shaped; fall through

            SerdeMetadata.get runtimeType |> ignore
            JsonSerializer.Deserialize(json, runtimeType, JsonOptionsCache.defaultJsonOptions) :?> 'T
