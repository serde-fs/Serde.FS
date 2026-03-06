namespace Serde.FS

open System
open System.Collections.Concurrent

type TypeMetadata = { Type: Type }

module SerdeMetadata =
    let private registry = ConcurrentDictionary<Type, TypeMetadata>()

    let register (ty: Type) =
        registry.TryAdd(ty, { Type = ty }) |> ignore

    let get (ty: Type) : TypeMetadata =
        match registry.TryGetValue(ty) with
        | true, meta -> meta
        | false, _ ->
            let msg =
                $"Serde.FS: Missing metadata for type '{ty.FullName}'.\n\n" +
                "This type was inferred at runtime, but no metadata was generated for it.\n" +
                "Generic types require explicit type arguments when calling Deserialize<T>.\n\n" +
                $"Add `{ty.FullName}` to the call site to generate metadata."
            raise (SerdeMissingMetadataException(msg, ty))
