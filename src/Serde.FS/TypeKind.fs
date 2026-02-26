namespace Serde.FS

module TypeKindTypes =

    type PrimitiveKind =
        | Unit
        | Bool
        | Int8 | Int16 | Int32 | Int64
        | UInt8 | UInt16 | UInt32 | UInt64
        | Float32 | Float64
        | Decimal
        | String
        | Guid
        | DateTime
        | DateTimeOffset
        | TimeSpan
        | DateOnly
        | TimeOnly

    type AttributeInfo = {
        Name: string
        ConstructorArgs: obj list
        NamedArgs: (string * obj) list
    }

    type TypeKind =
        | Primitive of PrimitiveKind
        | Record of fields: FieldInfo list
        | Tuple of elements: FieldInfo list
        | Option of inner: TypeInfo
        | List of inner: TypeInfo
        | Array of inner: TypeInfo
        | Set of inner: TypeInfo
        | Map of key: TypeInfo * value: TypeInfo
        | Enum of namesAndValues: (string * int) list
        | AnonymousRecord of fields: FieldInfo list
        | Union of cases: UnionCase list

    and TypeInfo = {
        Namespace: string option
        EnclosingModules: string list
        TypeName: string
        Kind: TypeKind
        Attributes: AttributeInfo list
    }

    and FieldInfo = {
        Name: string
        Type: TypeInfo
        Attributes: AttributeInfo list
    }

    and UnionCase = {
        CaseName: string
        Fields: FieldInfo list
        Tag: int option
        Attributes: AttributeInfo list
    }

    let rec typeInfoToFSharpString (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> ti.TypeName
        | Option inner -> sprintf "%s option" (typeInfoToFSharpString inner)
        | List inner -> sprintf "%s list" (typeInfoToFSharpString inner)
        | Array inner -> sprintf "%s array" (typeInfoToFSharpString inner)
        | Set inner -> sprintf "Set<%s>" (typeInfoToFSharpString inner)
        | Map (key, value) -> sprintf "Map<%s, %s>" (typeInfoToFSharpString key) (typeInfoToFSharpString value)
        | Tuple elements ->
            elements |> List.map (fun f -> typeInfoToFSharpString f.Type) |> String.concat " * "
        | Record _ | AnonymousRecord _ | Union _ | Enum _ -> ti.TypeName

    let private upperFirst (s: string) =
        if System.String.IsNullOrEmpty(s) then s
        else string (System.Char.ToUpperInvariant(s.[0])) + s.Substring(1)

    let private shortTypeName (ti: TypeInfo) =
        match ti.TypeName.LastIndexOf('.') with
        | -1 -> ti.TypeName
        | i -> ti.TypeName.Substring(i + 1)

    /// Converts a TypeInfo to a PascalCase identifier for module/file naming.
    /// e.g. int option → "IntOption", int option option → "IntOptionOption", MyApp.Person option → "PersonOption"
    let rec typeInfoToPascalName (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> upperFirst ti.TypeName
        | Option inner -> typeInfoToPascalName inner + "Option"
        | Record _ | AnonymousRecord _ | Union _ | Enum _ -> upperFirst (shortTypeName ti)
        | List inner -> typeInfoToPascalName inner + "List"
        | Array inner -> typeInfoToPascalName inner + "Array"
        | Set inner -> typeInfoToPascalName inner + "Set"
        | Map (key, value) -> typeInfoToPascalName key + typeInfoToPascalName value + "Map"
        | Tuple elements ->
            elements |> List.map (fun f -> typeInfoToPascalName f.Type) |> String.concat ""

    /// Produces fully-qualified F# type expressions for typeof<> / JsonTypeInfo<>.
    /// e.g. int option, MyApp.Person option, int option option
    let rec typeInfoToFqFSharpType (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> ti.TypeName
        | Option inner -> sprintf "%s option" (typeInfoToFqFSharpType inner)
        | Record _ | AnonymousRecord _ | Union _ | Enum _ ->
            let parts =
                [ yield! ti.Namespace |> Option.toList
                  yield! ti.EnclosingModules
                  yield ti.TypeName ]
            String.concat "." parts
        | List inner -> sprintf "%s list" (typeInfoToFqFSharpType inner)
        | Array inner -> sprintf "%s array" (typeInfoToFqFSharpType inner)
        | Set inner -> sprintf "Set<%s>" (typeInfoToFqFSharpType inner)
        | Map (key, value) -> sprintf "Map<%s, %s>" (typeInfoToFqFSharpType key) (typeInfoToFqFSharpType value)
        | Tuple elements ->
            elements |> List.map (fun f -> typeInfoToFqFSharpType f.Type) |> String.concat " * "
