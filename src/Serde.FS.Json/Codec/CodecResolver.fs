namespace Serde.FS.Json.Codec

open System
open Serde.FS

/// Resolves codecs for a given type using the deterministic pipeline:
/// 1. Type-level [<Serde(Codec = ...)>] or [<JsonCodec>] attribute
/// 2. Registry (manually registered + primitives)
/// 3. Error (SerdeCodecNotFoundException)
module CodecResolver =

    /// Checks for a [<JsonCodec>] attribute on the type and instantiates the codec if found.
    let private tryGetJsonCodecAttribute (ty: Type) : IJsonCodec option =
        match ty.GetCustomAttributes(typeof<JsonCodecAttribute>, false) with
        | [| attr |] ->
            let codecAttr = attr :?> JsonCodecAttribute
            let codecType = codecAttr.CodecType
            try
                match Activator.CreateInstance(codecType) with
                | :? IJsonCodec as codec -> Some codec
                | _ -> None
            with ex ->
                raise (SerdeCodecException($"Failed to instantiate codec type '{codecType.FullName}' from [<JsonCodec>] attribute.", ex))
        | _ -> None

    /// Checks for a [<Serde(Codec = ...)>] attribute on the type and instantiates the codec if found.
    let private tryGetSerdeCodecAttribute (ty: Type) : IJsonCodec option =
        match ty.GetCustomAttributes(typeof<SerdeAttribute>, false) with
        | [| attr |] ->
            let serdeAttr = attr :?> SerdeAttribute
            match serdeAttr.Codec with
            | null -> None
            | codecType ->
                try
                    match Activator.CreateInstance(codecType) with
                    | :? IJsonCodec as codec -> Some codec
                    | _ -> None
                with ex ->
                    raise (SerdeCodecException($"Failed to instantiate codec type '{codecType.FullName}' from [<Serde(Codec = ...)>] attribute.", ex))
        | _ -> None

    let private codecNotFound (ty: Type) =
        raise (SerdeCodecNotFoundException($"No codec found for type '{ty.FullName}'. Register a codec or factory.", ty))

    /// Resolves an untyped IJsonCodec for the given type.
    ///
    /// Resolution order:
    /// 1. Type-level codec attribute ([<Serde(Codec = ...)>] or [<JsonCodec>])
    /// 2. Registry lookup (includes manually registered and primitive codecs)
    /// 3. Throws SerdeCodecNotFoundException
    let resolve (ty: Type) (registry: CodecRegistry) : IJsonCodec =
        let directCodec =
            tryGetSerdeCodecAttribute ty
            |> Option.orElseWith (fun () -> tryGetJsonCodecAttribute ty)        // Step 1
            |> Option.orElseWith (fun () -> CodecRegistry.tryFind ty registry)  // Step 2

        match directCodec with
        | Some codec -> codec
        | None ->
            // Step 3a — Array factory
            if ty.IsArray && ty.GetArrayRank() = 1 then
                match CodecRegistry.tryFindFactory typeof<System.Array> registry with
                | Some factory -> factory [| ty.GetElementType() |] registry
                | None -> codecNotFound ty

            // Step 3b — Generic factory
            elif ty.IsGenericType then
                let genericDef = ty.GetGenericTypeDefinition()
                match CodecRegistry.tryFindFactory genericDef registry with
                | Some factory -> factory (ty.GetGenericArguments()) registry
                | None -> codecNotFound ty

            else
                codecNotFound ty

