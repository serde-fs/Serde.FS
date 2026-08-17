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

    /// Runs a reflection-based fallback factory. Under Native AOT / trimming the
    /// factory can fail for an instantiation that was never compiled or a member
    /// that was trimmed; surface that as a codec-not-found error that names the
    /// type and explains the fix, instead of a bare NullReference / NotSupported.
    let private runFactory (ty: Type) (factory: CodecFactory) (typeArgs: Type[]) (registry: CodecRegistry) : IJsonCodec =
        try
            factory typeArgs registry
        with
        | :? SerdeCodecNotFoundException -> reraise ()   // an element type is missing — keep that message
        | ex ->
            raise (
                SerdeCodecNotFoundException(
                    $"No statically-typed codec is registered for '{ty.FullName}' and the reflection-based fallback failed " +
                    $"({ex.GetType().Name}: {ex.Message}). This happens under Native AOT / trimming when a generic instantiation " +
                    "was not visible to the Serde.FS source generator (for example a type only ever constructed at runtime). " +
                    "Reference the closed type from a [<Serde>] field, an [<RpcApi>] method signature, or an explicit " +
                    "SerdeJson.serialize<T> call so the generator emits its codec, or register a codec for it manually.",
                    ty))

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
                | Some factory -> runFactory ty factory [| ty.GetElementType() |] registry
                | None -> codecNotFound ty

            // Step 3b — Generic factory
            elif ty.IsGenericType then
                let genericDef = ty.GetGenericTypeDefinition()
                match CodecRegistry.tryFindFactory genericDef registry with
                | Some factory -> runFactory ty factory (ty.GetGenericArguments()) registry
                | None -> codecNotFound ty

            else
                codecNotFound ty

