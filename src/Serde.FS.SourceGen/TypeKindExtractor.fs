namespace Serde.FS.SourceGen

open Serde.FS.TypeKindTypes
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

module TypeKindExtractor =

    let private checker = FSharpChecker.Create()

    let private primitiveMap =
        Map.ofList [
            "unit", Unit
            "bool", Bool
            "sbyte", Int8; "int8", Int8
            "int16", Int16
            "int", Int32; "int32", Int32
            "int64", Int64
            "byte", UInt8; "uint8", UInt8
            "uint16", UInt16
            "uint32", UInt32
            "uint64", UInt64
            "float32", Float32; "single", Float32
            "float", Float64; "double", Float64
            "decimal", Decimal
            "string", String
            "System.Guid", Guid; "Guid", Guid
            "System.DateTime", DateTime; "DateTime", DateTime
            "System.DateTimeOffset", DateTimeOffset; "DateTimeOffset", DateTimeOffset
            "System.TimeSpan", TimeSpan; "TimeSpan", TimeSpan
            "System.DateOnly", DateOnly; "DateOnly", DateOnly
            "System.TimeOnly", TimeOnly; "TimeOnly", TimeOnly
        ]

    let private optionNames = set [ "option"; "Option"; "FSharp.Core.option" ]
    let private listNames = set [ "list"; "List"; "FSharp.Collections.list" ]
    let private arrayNames = set [ "array"; "Array" ]
    let private setNames = set [ "Set"; "FSharp.Collections.Set" ]
    let private mapNames = set [ "Map"; "FSharp.Collections.Map" ]

    let private identToString (idents: LongIdent) =
        idents |> List.map (fun i -> i.idText) |> String.concat "."

    let private normalizeAttrName (name: string) =
        if name.EndsWith("Attribute") then name
        else name + "Attribute"

    let private extractObjConst (expr: SynExpr) : obj option =
        match expr with
        | SynExpr.Const(SynConst.String(s, _, _), _) -> Some (box s)
        | SynExpr.Const(SynConst.Int32 i, _) -> Some (box i)
        | SynExpr.Const(SynConst.Bool b, _) -> Some (box b)
        | SynExpr.Const(SynConst.Double d, _) -> Some (box d)
        | SynExpr.Const(SynConst.Single f, _) -> Some (box f)
        | SynExpr.Const(SynConst.Int64 i, _) -> Some (box i)
        | SynExpr.Const(SynConst.Char c, _) -> Some (box c)
        | _ -> None

    let private tryExtractNamedArg (expr: SynExpr) : (string * obj) option =
        match expr with
        // Pattern: Name = value (infix op_Equality application)
        | SynExpr.App(_, _,
            SynExpr.App(_, true, _, SynExpr.Ident(nameIdent), _),
            valueExpr, _) ->
                match extractObjConst valueExpr with
                | Some v -> Some (nameIdent.idText, v)
                | None -> None
        | _ -> None

    let private classifyArg (expr: SynExpr) : Choice<obj, string * obj> option =
        match tryExtractNamedArg expr with
        | Some na -> Some (Choice2Of2 na)
        | None ->
            match extractObjConst expr with
            | Some v -> Some (Choice1Of2 v)
            | None -> None

    let private extractAttributeArgs (argExpr: SynExpr) : obj list * (string * obj) list =
        let constructorArgs = ResizeArray<obj>()
        let namedArgs = ResizeArray<string * obj>()

        let processArg (expr: SynExpr) =
            match classifyArg expr with
            | Some (Choice1Of2 v) -> constructorArgs.Add(v)
            | Some (Choice2Of2 na) -> namedArgs.Add(na)
            | None -> ()

        match argExpr with
        | SynExpr.Const(SynConst.Unit, _) -> ()
        | SynExpr.Paren(SynExpr.Const(SynConst.Unit, _), _, _, _) -> ()
        | SynExpr.Paren(SynExpr.Tuple(_, exprs, _, _), _, _, _) ->
            for expr in exprs do processArg expr
        | SynExpr.Paren(inner, _, _, _) ->
            processArg inner
        | other ->
            processArg other

        (Seq.toList constructorArgs, Seq.toList namedArgs)

    let private extractAttributeInfo (attr: SynAttribute) : AttributeInfo =
        let rawName =
            match attr.TypeName with
            | SynLongIdent(id = idents) ->
                idents |> List.map (fun i -> i.idText) |> String.concat "."
        let name = normalizeAttrName rawName
        let ctorArgs, namedArgs = extractAttributeArgs attr.ArgExpr
        { Name = name; ConstructorArgs = ctorArgs; NamedArgs = namedArgs }

    let private extractAttributes (attrs: SynAttributes) : AttributeInfo list =
        attrs
        |> List.collect (fun attrList -> attrList.Attributes)
        |> List.map extractAttributeInfo

    let rec private synTypeToTypeInfo (synType: SynType) : TypeInfo =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            match Map.tryFind name primitiveMap with
            | Some pk ->
                { Namespace = None; EnclosingModules = []; TypeName = name; Kind = Primitive pk; Attributes = [] }
            | None ->
                { Namespace = None; EnclosingModules = []; TypeName = name; Kind = Record []; Attributes = [] }

        | SynType.App(typeName, _, typeArgs, _, _, _isPostfix, _) ->
            let baseName =
                match typeName with
                | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
                | _ -> "unknown"

            if optionNames.Contains baseName then
                match typeArgs with
                | [inner] ->
                    { Namespace = None; EnclosingModules = []; TypeName = "option"
                      Kind = Option(synTypeToTypeInfo inner); Attributes = [] }
                | _ ->
                    { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

            elif listNames.Contains baseName then
                match typeArgs with
                | [inner] ->
                    { Namespace = None; EnclosingModules = []; TypeName = "list"
                      Kind = List(synTypeToTypeInfo inner); Attributes = [] }
                | _ ->
                    { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

            elif arrayNames.Contains baseName then
                match typeArgs with
                | [inner] ->
                    { Namespace = None; EnclosingModules = []; TypeName = "array"
                      Kind = Array(synTypeToTypeInfo inner); Attributes = [] }
                | _ ->
                    { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

            elif setNames.Contains baseName then
                match typeArgs with
                | [inner] ->
                    { Namespace = None; EnclosingModules = []; TypeName = "Set"
                      Kind = Set(synTypeToTypeInfo inner); Attributes = [] }
                | _ ->
                    { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

            elif mapNames.Contains baseName then
                match typeArgs with
                | [keyType; valueType] ->
                    { Namespace = None; EnclosingModules = []; TypeName = "Map"
                      Kind = Map(synTypeToTypeInfo keyType, synTypeToTypeInfo valueType); Attributes = [] }
                | _ ->
                    { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

            else
                { Namespace = None; EnclosingModules = []; TypeName = baseName; Kind = Record []; Attributes = [] }

        | SynType.Tuple(_isStruct, segments, _) ->
            let types =
                segments
                |> List.choose (fun seg ->
                    match seg with
                    | SynTupleTypeSegment.Type t -> Some t
                    | _ -> None)
            let fields =
                types
                |> List.mapi (fun i t ->
                    { Name = sprintf "Item%d" (i + 1); Type = synTypeToTypeInfo t; Attributes = [] })
            { Namespace = None; EnclosingModules = []; TypeName = "tuple"; Kind = Tuple fields; Attributes = [] }

        | SynType.Array(_, elementType, _) ->
            { Namespace = None; EnclosingModules = []; TypeName = "array"
              Kind = Array(synTypeToTypeInfo elementType); Attributes = [] }

        | SynType.AnonRecd(_isStruct, fields, _) ->
            let fieldInfos =
                fields
                |> List.map (fun (ident, synTy) ->
                    { Name = ident.idText; Type = synTypeToTypeInfo synTy; Attributes = [] })
            { Namespace = None; EnclosingModules = []; TypeName = ""; Kind = AnonymousRecord fieldInfos; Attributes = [] }

        | SynType.Paren(innerType, _) ->
            synTypeToTypeInfo innerType

        | _ ->
            { Namespace = None; EnclosingModules = []; TypeName = "unknown"; Kind = Record []; Attributes = [] }

    let private extractRecordFields (fields: SynField list) : FieldInfo list =
        fields
        |> List.map (fun (SynField(attrs, _, idOpt, fieldType, _, _, _, _, _)) ->
            let name =
                match idOpt with
                | Some ident -> ident.idText
                | None -> "unknown"
            { Name = name; Type = synTypeToTypeInfo fieldType; Attributes = extractAttributes attrs })

    let private extractEnumCases (cases: SynEnumCase list) : (string * int) list =
        cases
        |> List.map (fun (SynEnumCase(_, SynIdent(ident, _), valueExpr, _, _, _)) ->
            let value =
                match valueExpr with
                | SynExpr.Const(SynConst.Int32 v, _) -> v
                | _ -> 0
            (ident.idText, value))

    let private extractUnionCaseFields (caseType: SynUnionCaseKind) : FieldInfo list =
        match caseType with
        | SynUnionCaseKind.Fields fields ->
            fields
            |> List.map (fun (SynField(attrs, _, idOpt, fieldType, _, _, _, _, _)) ->
                let name =
                    match idOpt with
                    | Some ident -> ident.idText
                    | None -> "Item"
                { Name = name; Type = synTypeToTypeInfo fieldType; Attributes = extractAttributes attrs })
        | SynUnionCaseKind.FullType _ -> []

    let private extractUnionCases (cases: SynUnionCase list) : UnionCase list =
        cases
        |> List.mapi (fun i (SynUnionCase(attrs, SynIdent(ident, _), caseType, _, _, _, _)) ->
            { CaseName = ident.idText
              Fields = extractUnionCaseFields caseType
              Tag = Some i
              Attributes = extractAttributes attrs })

    let private extractNamespace (longId: LongIdent) =
        longId |> List.map (fun i -> i.idText) |> String.concat "."

    let private processTypeDefn (ns: string option) (modules: string list) (typeDefn: SynTypeDefn) : TypeInfo option =
        let (SynTypeDefn(typeInfo = synComponentInfo; typeRepr = typeRepr)) = typeDefn
        let (SynComponentInfo(attributes = attrs; longId = typeNameIdent)) = synComponentInfo
        let typeName = typeNameIdent |> List.map (fun i -> i.idText) |> String.concat "."
        let typeAttrs = extractAttributes attrs

        match typeRepr with
        | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Record(_, fields, _), _) ->
            Some {
                Namespace = ns
                EnclosingModules = modules
                TypeName = typeName
                Kind = Record(extractRecordFields fields)
                Attributes = typeAttrs
            }
        | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Union(_, cases, _), _) ->
            Some {
                Namespace = ns
                EnclosingModules = modules
                TypeName = typeName
                Kind = Union(extractUnionCases cases)
                Attributes = typeAttrs
            }
        | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Enum(cases, _), _) ->
            Some {
                Namespace = ns
                EnclosingModules = modules
                TypeName = typeName
                Kind = Enum(extractEnumCases cases)
                Attributes = typeAttrs
            }
        | _ -> None

    let rec private walkDecls (ns: string option) (modules: string list) (decls: SynModuleDecl list) : TypeInfo list =
        [ for decl in decls do
            match decl with
            | SynModuleDecl.Types(typeDefns, _) ->
                for typeDefn in typeDefns do
                    match processTypeDefn ns modules typeDefn with
                    | Some info -> yield info
                    | None -> ()
            | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = moduleIdent); decls = nestedDecls) ->
                let moduleName = moduleIdent |> List.map (fun i -> i.idText) |> String.concat "."
                yield! walkDecls ns (modules @ [moduleName]) nestedDecls
            | _ -> () ]

    let private parseTree (filePath: string) (sourceText: string) : TypeInfo list =
        let source = SourceText.ofString sourceText

        let parsingOptions =
            { FSharpParsingOptions.Default with
                SourceFiles = [| filePath |] }

        let parseResults =
            checker.ParseFile(filePath, source, parsingOptions)
            |> Async.RunSynchronously

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            [ for SynModuleOrNamespace(longId = nsId; kind = kind; decls = decls) in modules do
                match kind with
                | SynModuleOrNamespaceKind.NamedModule ->
                    let moduleNames = nsId |> List.map (fun i -> i.idText)
                    yield! walkDecls None moduleNames decls
                | SynModuleOrNamespaceKind.DeclaredNamespace ->
                    let ns = Some(extractNamespace nsId)
                    yield! walkDecls ns [] decls
                | _ ->
                    yield! walkDecls None [] decls ]
        | _ -> []

    /// Extract TypeInfo metadata for all types in the given F# source.
    let extractTypes (filePath: string) (sourceText: string) : TypeInfo list =
        parseTree filePath sourceText
