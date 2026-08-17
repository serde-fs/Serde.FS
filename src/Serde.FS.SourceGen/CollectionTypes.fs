namespace Serde.FS.SourceGen

open FSharp.SourceDjinn.TypeModel.Types

/// Built-in collection / wrapper shapes that Serde.FS.Json can serialize
/// without a user-declared [<Serde>] definition. At runtime these are served by
/// reflection-based codec factories; for every *closed* instantiation the
/// generator can see at compile time it instead emits a statically-typed codec
/// (issue #14) so the pipeline works under Native AOT / trimming.
type CollectionShape =
    | ListOf of TypeInfo
    | ArrayOf of TypeInfo
    | SeqOf of TypeInfo
    | SetOf of TypeInfo
    | MapOf of key: TypeInfo * value: TypeInfo
    | ResultOf of ok: TypeInfo * error: TypeInfo

module CollectionTypes =

    /// Classifies a TypeInfo as one of the built-in collection shapes.
    /// Understands both parser representations of `seq<T>`:
    ///   • record-field parser: ConstructedGenericType with TypeName "seq"
    ///   • RPC-signature parser: TypeKind.List with TypeName "seq" (collapsed for
    ///     wire compatibility, TypeName preserved so it can be told apart)
    let (|Shape|_|) (ti: TypeInfo) : CollectionShape option =
        match ti.Kind with
        | List inner when ti.TypeName = "seq" || ti.TypeName = "Seq" -> Some (SeqOf inner)
        | List inner -> Some (ListOf inner)
        | Array inner -> Some (ArrayOf inner)
        | Set inner -> Some (SetOf inner)
        | Map (k, v) -> Some (MapOf (k, v))
        | ConstructedGenericType ->
            match ti.TypeName, ti.GenericArguments with
            | ("seq" | "Seq"), [ inner ] -> Some (SeqOf inner)
            | "Result", [ ok; err ] -> Some (ResultOf (ok, err))
            | _ -> None
        | _ -> None

    /// True when the type (transitively) mentions a generic parameter, i.e. it is
    /// not a closed instantiation and cannot be spelled in a `typeof<...>`.
    let rec containsGenericParameter (ti: TypeInfo) : bool =
        match ti.Kind with
        | GenericParameter _ | GenericTypeDefinition _ -> true
        | Option inner | List inner | Array inner | Set inner -> containsGenericParameter inner
        | Map (k, v) -> containsGenericParameter k || containsGenericParameter v
        | Tuple elems | Record elems | AnonymousRecord elems ->
            elems |> List.exists (fun f -> containsGenericParameter f.Type)
        | Union cases ->
            cases |> List.exists (fun c -> c.Fields |> List.exists (fun f -> containsGenericParameter f.Type))
        | ConstructedGenericType -> ti.GenericArguments |> List.exists containsGenericParameter
        | Primitive _ | Enum _ -> false

    /// `byte[]` has a dedicated primitive codec (base64 string) registered by the
    /// runtime; the generator must never shadow it with an element-wise array codec.
    let isByteArray (ti: TypeInfo) =
        match ti.Kind with
        | Array { Kind = Primitive UInt8 } -> true
        | _ -> false

    /// Rewrites every seq<T> occurrence into the canonical ConstructedGenericType
    /// form so `typeInfoToFqFSharpType` prints `seq<T>` (not `T list`) and
    /// `typeInfoToPascalName` yields `SeqT` (distinct from `TList`).
    let rec normalize (ti: TypeInfo) : TypeInfo =
        let seqOf inner =
            { Namespace = None; EnclosingModules = []; TypeName = "seq"; Kind = ConstructedGenericType
              Attributes = []; GenericParameters = []; GenericArguments = [ normalize inner ] }
        match ti.Kind with
        | List inner when ti.TypeName = "seq" || ti.TypeName = "Seq" -> seqOf inner
        | ConstructedGenericType when (ti.TypeName = "seq" || ti.TypeName = "Seq") && ti.GenericArguments.Length = 1 ->
            seqOf ti.GenericArguments.Head
        | ConstructedGenericType ->
            { ti with GenericArguments = ti.GenericArguments |> List.map normalize }
        | Option inner -> { ti with Kind = Option (normalize inner) }
        | List inner -> { ti with Kind = List (normalize inner) }
        | Array inner -> { ti with Kind = Array (normalize inner) }
        | Set inner -> { ti with Kind = Set (normalize inner) }
        | Map (k, v) -> { ti with Kind = Map (normalize k, normalize v) }
        | Tuple elems -> { ti with Kind = Tuple (elems |> List.map (fun f -> { f with Type = normalize f.Type })) }
        | _ -> ti
