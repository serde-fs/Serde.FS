namespace Serde.FS.SourceGen

open Serde.FS
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

module AstParser =

    let private checker = FSharpChecker.Create()

    let private serdeAttributeNames =
        set [
            "Serde"; "SerdeAttribute"
            "SerdeSerialize"; "SerdeSerializeAttribute"
            "SerdeDeserialize"; "SerdeDeserializeAttribute"
        ]

    let private shortName (name: string) =
        match name.LastIndexOf('.') with
        | -1 -> name
        | i -> name.Substring(i + 1)

    let private getCapability (attrNames: string list) =
        let shortNames = attrNames |> List.map shortName
        let hasSerialize =
            shortNames |> List.exists (fun n -> n = "Serde" || n = "SerdeAttribute" || n = "SerdeSerialize" || n = "SerdeSerializeAttribute")
        let hasDeserialize =
            shortNames |> List.exists (fun n -> n = "Serde" || n = "SerdeAttribute" || n = "SerdeDeserialize" || n = "SerdeDeserializeAttribute")
        match hasSerialize, hasDeserialize with
        | true, true -> Some Both
        | true, false -> Some Serialize
        | false, true -> Some Deserialize
        | false, false -> None

    let private getAttributeName (attr: SynAttribute) =
        match attr.TypeName with
        | SynLongIdent(id = idents) ->
            idents |> List.map (fun i -> i.idText) |> String.concat "."

    let private findSerdeAttributes (attrs: SynAttributes) =
        attrs
        |> List.collect (fun attrList -> attrList.Attributes)
        |> List.map getAttributeName
        |> List.filter (fun name -> serdeAttributeNames.Contains(shortName name))

    let rec private synTypeToString (synType: SynType) : string =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            idents |> List.map (fun i -> i.idText) |> String.concat "."

        | SynType.App(typeName, _, typeArgs, _, _, isPostfix, _) ->
            let baseName = synTypeToString typeName
            let args = typeArgs |> List.map synTypeToString
            if isPostfix then
                match args with
                | [arg] -> sprintf "%s %s" arg baseName
                | _ -> sprintf "(%s) %s" (args |> String.concat ", ") baseName
            else
                sprintf "%s<%s>" baseName (args |> String.concat ", ")

        | SynType.Paren(innerType, _) ->
            synTypeToString innerType

        | _ ->
            sprintf "%A" synType

    let private extractFields (fields: SynField list) =
        fields
        |> List.map (fun (SynField(_, _, idOpt, fieldType, _, _, _, _, _)) ->
            let name =
                match idOpt with
                | Some ident -> ident.idText
                | None -> failwithf "Record field has no name"
            { Name = name; FSharpType = synTypeToString fieldType })

    let private extractNamespace (longId: LongIdent) =
        longId |> List.map (fun i -> i.idText) |> String.concat "."

    let private processTypeDefn (ns: string option) (modules: string list) (typeDefn: SynTypeDefn) : SerdeTypeInfo option =
        let (SynTypeDefn(typeInfo = synComponentInfo; typeRepr = typeRepr)) = typeDefn
        let (SynComponentInfo(attributes = attrs; longId = typeNameIdent)) = synComponentInfo

        let serdeAttrs = findSerdeAttributes attrs

        match serdeAttrs with
        | [] -> None
        | _ ->
            let capability = getCapability serdeAttrs
            match capability, typeRepr with
            | Some cap, SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Record(_, fields, _), _) ->
                let typeName = typeNameIdent |> List.map (fun i -> i.idText) |> String.concat "."
                Some {
                    Namespace = ns
                    EnclosingModules = modules
                    TypeName = typeName
                    Capability = cap
                    Fields = extractFields fields
                }
            | _ -> None

    let rec private walkDecls (ns: string option) (modules: string list) (decls: SynModuleDecl list) : SerdeTypeInfo list =
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

    let private parseTree (filePath: string) (sourceText: string) : SerdeTypeInfo list =
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
                    let ns = Some (extractNamespace nsId)
                    yield! walkDecls ns [] decls
                | _ ->
                    yield! walkDecls None [] decls ]
        | _ -> []

    /// Parse F# source text and return all Serde-annotated types found.
    let parseSource (filePath: string) (sourceText: string) : SerdeTypeInfo list =
        parseTree filePath sourceText

    /// Parse an F# source file and return all Serde-annotated types found.
    let parseFile (filePath: string) : SerdeTypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        parseTree filePath sourceText

    /// Check if a long ident matches "SerdeApp.entryPoint" or "Serde.FS.SerdeApp.entryPoint".
    let private isEntryPointIdent (idents: LongIdent) =
        let names = idents |> List.map (fun i -> i.idText)
        match names with
        | [ "SerdeApp"; "entryPoint" ] -> true
        | [ "Serde"; "FS"; "SerdeApp"; "entryPoint" ] -> true
        | _ -> false

    /// Recursively check if an expression contains a call to SerdeApp.registerEntryPoint.
    let rec private exprContainsEntryPointRegistration (expr: SynExpr) =
        match expr with
        | SynExpr.App(_, _, funcExpr, argExpr, _) ->
            exprContainsEntryPointRegistration funcExpr
            || exprContainsEntryPointRegistration argExpr
        | SynExpr.LongIdent(_, SynLongIdent(id = idents), _, _) ->
            isEntryPointIdent idents
        | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
            exprContainsEntryPointRegistration e1
            || exprContainsEntryPointRegistration e2
        | SynExpr.Do(expr = e) ->
            exprContainsEntryPointRegistration e
        | SynExpr.Paren(expr = e) ->
            exprContainsEntryPointRegistration e
        | SynExpr.LetOrUse(body = body) ->
            exprContainsEntryPointRegistration body
        | _ -> false

    /// Walk declarations looking for entry point registrations.
    let rec private declsContainEntryPointRegistration (decls: SynModuleDecl list) =
        decls |> List.exists (fun decl ->
            match decl with
            | SynModuleDecl.Expr(expr, _) ->
                exprContainsEntryPointRegistration expr
            | SynModuleDecl.NestedModule(decls = nestedDecls) ->
                declsContainEntryPointRegistration nestedDecls
            | SynModuleDecl.Let(_, bindings, _) ->
                bindings |> List.exists (fun (SynBinding(expr = expr)) ->
                    exprContainsEntryPointRegistration expr)
            | _ -> false)

    /// Check if source text contains a call to SerdeApp.registerEntryPoint.
    let hasEntryPointRegistration (filePath: string) (sourceText: string) : bool =
        let source = SourceText.ofString sourceText

        let parsingOptions =
            { FSharpParsingOptions.Default with
                SourceFiles = [| filePath |] }

        let parseResults =
            checker.ParseFile(filePath, source, parsingOptions)
            |> Async.RunSynchronously

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            modules |> List.exists (fun (SynModuleOrNamespace(decls = decls)) ->
                declsContainEntryPointRegistration decls)
        | _ -> false

    /// Check if a source file contains a call to SerdeApp.registerEntryPoint.
    let hasEntryPointRegistrationInFile (filePath: string) : bool =
        let sourceText = System.IO.File.ReadAllText(filePath)
        hasEntryPointRegistration filePath sourceText
