namespace FSharp.SourceDjinn.TypeModel

module Types =

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

    type GenericConstraint =
        | RequiresStruct
        | RequiresClass
        | RequiresDefaultConstructor
        | SubtypeOf of TypeInfo
        | MemberConstraint of string

    and GenericParameterInfo = {
        Name: string
        Constraints: GenericConstraint list
    }

    and TypeKind =
        | Primitive of PrimitiveKind
        | Record of fields: FieldInfo list
        | Tuple of elements: FieldInfo list
        | Option of inner: TypeInfo
        | List of inner: TypeInfo
        | Array of inner: TypeInfo
        | Set of inner: TypeInfo
        | Map of key: TypeInfo * value: TypeInfo
        | Enum of cases: EnumCase list
        | AnonymousRecord of fields: FieldInfo list
        | Union of cases: UnionCase list
        | GenericParameter of name: string
        | GenericTypeDefinition of arity: int
        | ConstructedGenericType

    and TypeInfo = {
        Namespace: string option
        EnclosingModules: string list
        TypeName: string
        Kind: TypeKind
        Attributes: AttributeInfo list
        GenericParameters: GenericParameterInfo list
        GenericArguments: TypeInfo list
    }

    and FieldInfo = {
        Name: string
        Type: TypeInfo
        Attributes: AttributeInfo list
    }

    and EnumCase = {
        CaseName: string
        Value: int
        Attributes: AttributeInfo list
    }

    and UnionCase = {
        CaseName: string
        Fields: FieldInfo list
        Tag: int option
        Attributes: AttributeInfo list
    }

    let private genericParamSuffix (ti: TypeInfo) =
        if ti.GenericParameters.IsEmpty then ""
        else
            let paramNames = ti.GenericParameters |> List.map (fun p -> sprintf "'%s" p.Name)
            sprintf "<%s>" (System.String.Join(", ", paramNames))

    let private genericArgSuffix (ti: TypeInfo) (fmt: TypeInfo -> string) =
        if ti.GenericArguments.IsEmpty then ""
        else
            let argNames = ti.GenericArguments |> List.map fmt
            sprintf "<%s>" (System.String.Join(", ", argNames))

    let rec typeInfoToFSharpString (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> ti.TypeName
        | GenericParameter name -> sprintf "'%s" name
        | GenericTypeDefinition _ -> ti.TypeName + genericParamSuffix ti
        | ConstructedGenericType -> ti.TypeName + genericArgSuffix ti typeInfoToFSharpString
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
    /// e.g. int option -> "IntOption", int option option -> "IntOptionOption", MyApp.Person option -> "PersonOption"
    let rec typeInfoToPascalName (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> upperFirst ti.TypeName
        | GenericParameter name -> upperFirst name
        | GenericTypeDefinition _ ->
            let baseName = upperFirst (shortTypeName ti)
            let paramPart = ti.GenericParameters |> List.map (fun p -> upperFirst p.Name) |> System.String.Concat
            baseName + paramPart
        | ConstructedGenericType ->
            let baseName = upperFirst (shortTypeName ti)
            let argPart = ti.GenericArguments |> List.map typeInfoToPascalName |> System.String.Concat
            baseName + argPart
        | Option inner -> typeInfoToPascalName inner + "Option"
        | Record _ | AnonymousRecord _ | Union _ | Enum _ -> upperFirst (shortTypeName ti)
        | List inner -> typeInfoToPascalName inner + "List"
        | Array inner -> typeInfoToPascalName inner + "Array"
        | Set inner -> typeInfoToPascalName inner + "Set"
        | Map (key, value) -> typeInfoToPascalName key + typeInfoToPascalName value + "Map"
        | Tuple elements ->
            (elements |> List.map (fun f -> typeInfoToPascalName f.Type) |> String.concat "") + "Tuple"

    /// Produces fully-qualified F# type expressions for typeof<> / JsonTypeInfo<>.
    /// e.g. int option, MyApp.Person option, int option option
    let rec typeInfoToFqFSharpType (ti: TypeInfo) : string =
        match ti.Kind with
        | Primitive _ -> ti.TypeName
        | GenericParameter name -> sprintf "'%s" name
        | GenericTypeDefinition _ ->
            let baseParts =
                [ yield! ti.Namespace |> Option.toList
                  yield! ti.EnclosingModules
                  yield ti.TypeName ]
            let baseName = System.String.Join(".", baseParts)
            baseName + genericParamSuffix ti
        | ConstructedGenericType ->
            let baseParts =
                [ yield! ti.Namespace |> Option.toList
                  yield! ti.EnclosingModules
                  yield ti.TypeName ]
            let baseName = System.String.Join(".", baseParts)
            baseName + genericArgSuffix ti typeInfoToFqFSharpType
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
            "(" + (elements |> List.map (fun f -> typeInfoToFqFSharpType f.Type) |> String.concat " * ") + ")"

    module TypeInfo =
        let rec private substituteTypeInfo (paramMap: Map<string, TypeInfo>) (ti: TypeInfo) : TypeInfo =
            match ti.Kind with
            | GenericParameter name ->
                match Map.tryFind name paramMap with
                | Some replacement -> replacement
                | None -> ti
            | Record fields ->
                { ti with Kind = Record (fields |> List.map (substituteFieldInfo paramMap)) }
            | Tuple elements ->
                { ti with Kind = Tuple (elements |> List.map (substituteFieldInfo paramMap)) }
            | Option inner ->
                { ti with Kind = Option (substituteTypeInfo paramMap inner) }
            | List inner ->
                { ti with Kind = List (substituteTypeInfo paramMap inner) }
            | Array inner ->
                { ti with Kind = Array (substituteTypeInfo paramMap inner) }
            | Set inner ->
                { ti with Kind = Set (substituteTypeInfo paramMap inner) }
            | Map (key, value) ->
                { ti with Kind = Map (substituteTypeInfo paramMap key, substituteTypeInfo paramMap value) }
            | AnonymousRecord fields ->
                { ti with Kind = AnonymousRecord (fields |> List.map (substituteFieldInfo paramMap)) }
            | Union cases ->
                let newCases = cases |> List.map (fun c -> { c with Fields = c.Fields |> List.map (substituteFieldInfo paramMap) })
                { ti with Kind = Union newCases }
            | Primitive _ | Enum _ | GenericTypeDefinition _ | ConstructedGenericType -> ti

        and private substituteFieldInfo (paramMap: Map<string, TypeInfo>) (fi: FieldInfo) : FieldInfo =
            { fi with Type = substituteTypeInfo paramMap fi.Type }

        let instantiate (definition: TypeInfo) (args: TypeInfo list) : TypeInfo =
            if definition.GenericParameters.Length <> args.Length || definition.GenericParameters.IsEmpty then
                invalidArg "definition" "TypeInfo must be a generic definition with matching arity"
            else
                let paramMap =
                    List.zip (definition.GenericParameters |> List.map (fun p -> p.Name)) args
                    |> Map.ofList
                let substituted = substituteTypeInfo paramMap definition
                { substituted with
                    GenericParameters = []
                    GenericArguments = args }

    type TypeInfo with
        member x.IsGenericDefinition =
            not x.GenericParameters.IsEmpty

        member x.IsConstructedGeneric =
            not x.GenericArguments.IsEmpty
