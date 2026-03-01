namespace Serde.FS

open FSharp.SourceDjinn.Types

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

type SerdeTypeInfo = {
    Raw: TypeInfo
    Capability: SerdeCapability
    Attributes: SerdeAttributes
    Fields: SerdeFieldInfo list option
    UnionCases: SerdeUnionCaseInfo list option
    EnumCases: SerdeEnumCaseInfo list option
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

    let private buildSerdeFieldInfo (typeCap: SerdeCapability) (fi: FieldInfo) : SerdeFieldInfo =
        let attrs = buildSerdeAttributes fi.Attributes
        let effectiveName = attrs.Rename |> Option.defaultValue fi.Name
        {
            Name = effectiveName
            RawName = fi.Name
            Type = fi.Type
            Attributes = attrs
            Capability = resolveFieldCapability typeCap attrs
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

    let buildSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        let capability = resolveCapability ti.Attributes
        let typeAttrs = buildSerdeAttributes ti.Attributes
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
            Fields = fields
            UnionCases = unionCases
            EnumCases = enumCases
        }
