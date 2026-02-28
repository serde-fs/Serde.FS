namespace FSharp.SourceDjinn

open Serde.FS
open Serde.FS.TypeKindTypes
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

    let private isSerdeAnnotated (ti: TypeInfo) =
        ti.Attributes |> List.exists (fun a ->
            serdeAttributeNames.Contains(shortName a.Name))

    /// Parse F# source text and return all Serde-annotated types found.
    let parseSource (filePath: string) (sourceText: string) : SerdeTypeInfo list =
        TypeKindExtractor.extractTypes filePath sourceText
        |> List.filter isSerdeAnnotated
        |> List.map SerdeMetadataBuilder.buildSerdeTypeInfo

    /// Parse an F# source file and return all Serde-annotated types found.
    let parseFile (filePath: string) : SerdeTypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        parseSource filePath sourceText

    /// Parse an F# source file and return ALL type definitions (for building lookup maps).
    let parseFileAllTypes (filePath: string) : TypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        TypeKindExtractor.extractTypes filePath sourceText

    /// Check if a long ident matches "SerdeApp.entryPoint" or "Serde.FS.SerdeApp.entryPoint".
    let private isEntryPointIdent (idents: LongIdent) =
        let names = idents |> List.map (fun i -> i.idText)
        match names with
        | [ "SerdeApp"; "entryPoint" ] -> true
        | [ "Serde"; "FS"; "SerdeApp"; "entryPoint" ] -> true
        | _ -> false

    /// Recursively check if an expression contains a call to SerdeApp.entryPoint.
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

    /// Check if source text contains a call to SerdeApp.entryPoint.
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

    /// Check if a source file contains a call to SerdeApp.entryPoint.
    let hasEntryPointRegistrationInFile (filePath: string) : bool =
        let sourceText = System.IO.File.ReadAllText(filePath)
        hasEntryPointRegistration filePath sourceText
