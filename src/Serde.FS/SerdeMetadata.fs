namespace Serde.FS

open System
open System.Collections.Concurrent
open Microsoft.FSharp.Reflection

type TypeMetadata = { Type: Type }

module SerdeMetadata =
    let private registry = ConcurrentDictionary<Type, TypeMetadata>()

    let register (ty: Type) =
        registry.TryAdd(ty, { Type = ty }) |> ignore

    let tryFindGenericWrapperByCaseName (caseName: string) : string option =
        registry.Keys
        |> Seq.tryFind (fun ty ->
            ty.IsGenericType
            && not ty.IsGenericTypeDefinition
            && FSharpType.IsUnion(ty)
            && (let cases = FSharpType.GetUnionCases(ty)
                cases.Length = 1 && cases.[0].Name = caseName)
        )
        |> Option.map (fun ty -> ty.GetGenericTypeDefinition().Name.Split('`').[0])

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
