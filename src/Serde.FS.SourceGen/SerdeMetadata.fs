namespace Serde.FS

open FSharp.SourceDjinn
open FSharp.SourceDjinn.TypeModel.Types

type SerdeCapability =
    | Serialize
    | Deserialize
    | Both

type SerdeAttributes = {
    Rename: string option
    Skip: bool
    SkipSerialize: bool
    SkipDeserialize: bool
}

module SerdeAttributes =
    let empty = {
        Rename = None
        Skip = false
        SkipSerialize = false
        SkipDeserialize = false
    }

type SerdeFieldInfo = {
    Name: string
    RawName: string
    Type: TypeInfo
    Attributes: SerdeAttributes
    Capability: SerdeCapability
    CodecType: string option
}

type SerdeEnumCaseInfo = {
    CaseName: string
    RawCaseName: string
    Value: int
    Attributes: SerdeAttributes
    Capability: SerdeCapability
}

type SerdeUnionCaseInfo = {
    CaseName: string
    RawCaseName: string
    Fields: SerdeFieldInfo list
    Tag: int option
    Attributes: SerdeAttributes
}

type SerdeGenericContext = {
    DefinitionType: TypeInfo
    GenericParameters: GenericParameterInfo list
    GenericArguments: TypeInfo list
}

type SerdeTypeInfo = {
    Raw: TypeInfo
    Capability: SerdeCapability
    Attributes: SerdeAttributes
    ConverterType: string option
    CodecType: string option
    Fields: SerdeFieldInfo list option
    UnionCases: SerdeUnionCaseInfo list option
    EnumCases: SerdeEnumCaseInfo list option
    GenericContext: SerdeGenericContext option
}

module SerdeMetadataBuilder =

    let private shortName (name: string) =
        match name.LastIndexOf('.') with
        | -1 -> name
        | i -> name.Substring(i + 1)

    let private resolveCapability (attrs: AttributeInfo list) : SerdeCapability =
        let names = attrs |> List.map (fun a -> shortName a.Name)
        let hasSer =
            names |> List.exists (fun n ->
                n = "Serde" || n = "SerdeAttribute" ||
                n = "SerdeSerialize" || n = "SerdeSerializeAttribute")
        let hasDeser =
            names |> List.exists (fun n ->
                n = "Serde" || n = "SerdeAttribute" ||
                n = "SerdeDeserialize" || n = "SerdeDeserializeAttribute")
        match hasSer, hasDeser with
        | true, true -> Both
        | true, false -> Serialize
        | false, true -> Deserialize
        | false, false -> Both

    let private buildSerdeAttributes (attrs: AttributeInfo list) : SerdeAttributes =
        let shortNames = attrs |> List.map (fun a -> shortName a.Name)
        let rename =
            attrs |> List.tryPick (fun a ->
                let sn = shortName a.Name
                if sn = "SerdeRename" || sn = "SerdeRenameAttribute" then
                    a.ConstructorArgs |> List.tryHead |> Option.bind (function :? string as s -> Some s | _ -> None)
                else None)
        {
            Rename = rename
            Skip = shortNames |> List.exists (fun n -> n = "SerdeSkip" || n = "SerdeSkipAttribute")
            SkipSerialize = shortNames |> List.exists (fun n -> n = "SerdeSkipSerialize" || n = "SerdeSkipSerializeAttribute")
            SkipDeserialize = shortNames |> List.exists (fun n -> n = "SerdeSkipDeserialize" || n = "SerdeSkipDeserializeAttribute")
        }

    let private resolveFieldCapability (typeCap: SerdeCapability) (attrs: SerdeAttributes) : SerdeCapability =
        if attrs.Skip then typeCap
        elif attrs.SkipSerialize then
            match typeCap with
            | Both -> Deserialize
            | Serialize -> Serialize
            | Deserialize -> Deserialize
        elif attrs.SkipDeserialize then
            match typeCap with
            | Both -> Serialize
            | Deserialize -> Deserialize
            | Serialize -> Serialize
        else typeCap

    let private extractCodecType (attrs: AttributeInfo list) : string option =
        attrs |> List.tryPick (fun a ->
            let sn = shortName a.Name
            if sn = "SerdeField" || sn = "SerdeFieldAttribute" then
                a.NamedArgs |> List.tryPick (fun (name, value) ->
                    if name = "Codec" then
                        match value with
                        | :? TypeKindExtractor.AttrArgValue as av ->
                            let (TypeKindExtractor.AttrArgValue.TypeOf fqn) = av
                            Some fqn
                        | _ -> None
                    else None)
            else None)

    let private buildSerdeFieldInfo (typeCap: SerdeCapability) (fi: FieldInfo) : SerdeFieldInfo =
        let attrs = buildSerdeAttributes fi.Attributes
        let effectiveName = attrs.Rename |> Option.defaultValue fi.Name
        let codecType = extractCodecType fi.Attributes
        {
            Name = effectiveName
            RawName = fi.Name
            Type = fi.Type
            Attributes = attrs
            Capability = resolveFieldCapability typeCap attrs
            CodecType = codecType
        }

    let private buildSerdeUnionCaseInfo (typeCap: SerdeCapability) (uc: UnionCase) : SerdeUnionCaseInfo =
        let attrs = buildSerdeAttributes uc.Attributes
        let effectiveName = attrs.Rename |> Option.defaultValue uc.CaseName
        {
            CaseName = effectiveName
            RawCaseName = uc.CaseName
            Fields = uc.Fields |> List.map (buildSerdeFieldInfo typeCap)
            Tag = uc.Tag
            Attributes = attrs
        }

    let private buildSerdeEnumCaseInfo (typeCap: SerdeCapability) (ec: EnumCase) : SerdeEnumCaseInfo =
        let attrs = buildSerdeAttributes ec.Attributes
        let effectiveName = attrs.Rename |> Option.defaultValue ec.CaseName
        {
            CaseName = effectiveName
            RawCaseName = ec.CaseName
            Value = ec.Value
            Attributes = attrs
            Capability = resolveFieldCapability typeCap attrs
        }

    let private extractSerdeNamedArg (argName: string) (attrs: AttributeInfo list) : string option =
        attrs |> List.tryPick (fun a ->
            let sn = shortName a.Name
            if sn = "Serde" || sn = "SerdeAttribute" then
                a.NamedArgs |> List.tryPick (fun (name, value) ->
                    if name = argName then
                        match value with
                        | :? TypeKindExtractor.AttrArgValue as av ->
                            let (TypeKindExtractor.AttrArgValue.TypeOf fqn) = av
                            Some fqn
                        | _ -> None
                    else None)
            else None)

    let buildSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        let capability = resolveCapability ti.Attributes
        let typeAttrs = buildSerdeAttributes ti.Attributes
        let converterType = extractSerdeNamedArg "Converter" ti.Attributes
        let codecType = extractSerdeNamedArg "Codec" ti.Attributes
        let fields, unionCases, enumCases =
            match ti.Kind with
            | Record fields | AnonymousRecord fields ->
                Some (fields |> List.map (buildSerdeFieldInfo capability)), None, None
            | Union cases ->
                None, Some (cases |> List.map (buildSerdeUnionCaseInfo capability)), None
            | Enum cases ->
                None, None, Some (cases |> List.map (buildSerdeEnumCaseInfo capability))
            | _ ->
                None, None, None
        {
            Raw = ti
            Capability = capability
            Attributes = typeAttrs
            ConverterType = converterType
            CodecType = codecType
            Fields = fields
            UnionCases = unionCases
            EnumCases = enumCases
            GenericContext = None
        }

/// Builds qualified references to union cases for generated pattern matches
/// and constructor calls. Shared by the Json and Fable emitters.
module UnionCaseNaming =

    let private shortName (name: string) =
        match name.LastIndexOf('.') with
        | -1 -> name
        | i -> name.Substring(i + 1)

    /// True when the type declaration carries [<RequireQualifiedAccess>].
    let hasRequireQualifiedAccess (info: SerdeTypeInfo) =
        info.Raw.Attributes
        |> List.exists (fun a ->
            let sn = shortName a.Name
            sn = "RequireQualifiedAccess" || sn = "RequireQualifiedAccessAttribute")

    /// True when generated pattern matches need a private type abbreviation
    /// (`type private XAlias = Ns.X` and `XAlias.Case`): the union carries
    /// [<RequireQualifiedAccess>] AND has a case named like the type itself.
    /// In pattern position neither `Ns.Type.Case` (FS1127 — the trailing
    /// same-named segments misresolve) nor `Ns.Case` (RQA keeps the
    /// constructor out of the enclosing scope) compiles; an abbreviation
    /// whose name differs from the case name is the only form that does.
    let needsAlias (info: SerdeTypeInfo) : bool =
        hasRequireQualifiedAccess info
        && (info.UnionCases
            |> Option.defaultValue []
            |> List.exists (fun c -> c.RawCaseName = info.Raw.TypeName))

    /// Qualified reference to a union case, e.g. "MyApp.Shared.Shape.Circle".
    ///
    /// The type segment is dropped when the case name equals the type name
    /// (the newtype idiom `type DocumentId = DocumentId of string`): the
    /// enclosing scope then contains both the type and the lifted constructor
    /// function, and in `Ns.DocumentId.DocumentId` the first ident binds to
    /// the constructor, so the emitted code fails to compile (issue #12).
    /// Constructed generics also drop it (`Ns.Type<'T>.Case` is not valid F#).
    /// [<RequireQualifiedAccess>] keeps the constructor out of the enclosing
    /// scope, so RQA keeps the `Ns.Type.Case` form — except when `needsAlias`
    /// is true, where the caller must emit an abbreviation and qualify cases
    /// with it instead of using this function's result in pattern position.
    let reference (info: SerdeTypeInfo) (case: SerdeUnionCaseInfo) : string =
        let includeTypeSegment =
            hasRequireQualifiedAccess info
            || (info.GenericContext.IsNone && case.RawCaseName <> info.Raw.TypeName)
        let parts =
            [ yield! info.Raw.Namespace |> Option.toList
              yield! info.Raw.EnclosingModules
              if includeTypeSegment then yield info.Raw.TypeName
              yield case.RawCaseName ]
        String.concat "." parts
