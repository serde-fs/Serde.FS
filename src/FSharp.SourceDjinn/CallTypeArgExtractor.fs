namespace FSharp.SourceDjinn

open FSharp.SourceDjinn.TypeModel.Types
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

module CallTypeArgExtractor =

    let private checker = FSharpChecker.Create()

    let private identToString (idents: LongIdent) =
        idents |> List.map (fun i -> i.idText) |> String.concat "."

    /// Match a long ident against the set of target function names.
    let private matchesFunctionName (functionNames: Set<string>) (lid: SynLongIdent) =
        match lid with
        | SynLongIdent(id = idents) ->
            let name = identToString idents
            functionNames.Contains name

    /// Recursively walk a SynExpr, collecting type args from matching TypeApp calls.
    let rec private walkExpr (functionNames: Set<string>) (acc: ResizeArray<TypeInfo>) (expr: SynExpr) =
        match expr with
        | SynExpr.TypeApp(SynExpr.LongIdent(_, lid, _, _), _, typeArgs, _, _, _, _) when matchesFunctionName functionNames lid ->
            for synType in typeArgs do
                acc.Add(TypeKindExtractor.synTypeToTypeInfo synType)
        | _ -> ()

        // Recurse into child expressions
        match expr with
        | SynExpr.App(_, _, funcExpr, argExpr, _) ->
            walkExpr functionNames acc funcExpr
            walkExpr functionNames acc argExpr
        | SynExpr.TypeApp(funcExpr, _, _, _, _, _, _) ->
            walkExpr functionNames acc funcExpr
        | SynExpr.LetOrUse(bindings = bindings; body = body) ->
            for binding in bindings do
                let (SynBinding(expr = bindExpr)) = binding
                walkExpr functionNames acc bindExpr
            walkExpr functionNames acc body
        | SynExpr.Sequential(_, _, expr1, expr2, _, _) ->
            walkExpr functionNames acc expr1
            walkExpr functionNames acc expr2
        | SynExpr.IfThenElse(ifExpr = ifExpr; thenExpr = thenExpr; elseExpr = elseExpr) ->
            walkExpr functionNames acc ifExpr
            walkExpr functionNames acc thenExpr
            elseExpr |> Option.iter (walkExpr functionNames acc)
        | SynExpr.Match(expr = expr; clauses = clauses)
        | SynExpr.MatchBang(expr = expr; clauses = clauses) ->
            walkExpr functionNames acc expr
            for (SynMatchClause(resultExpr = resultExpr)) in clauses do
                walkExpr functionNames acc resultExpr
        | SynExpr.Lambda(body = body) ->
            walkExpr functionNames acc body
        | SynExpr.Paren(expr = inner) ->
            walkExpr functionNames acc inner
        | SynExpr.Tuple(_, exprs, _, _) ->
            for e in exprs do walkExpr functionNames acc e
        | SynExpr.ArrayOrList(_, exprs, _) ->
            for e in exprs do walkExpr functionNames acc e
        | SynExpr.ArrayOrListComputed(_, expr, _) ->
            walkExpr functionNames acc expr
        | SynExpr.ComputationExpr(_, expr, _) ->
            walkExpr functionNames acc expr
        | SynExpr.Do(expr, _) ->
            walkExpr functionNames acc expr
        | SynExpr.DoBang(expr, _, _) ->
            walkExpr functionNames acc expr
        | SynExpr.TryWith(tryExpr = tryExpr; withCases = withCases) ->
            walkExpr functionNames acc tryExpr
            for (SynMatchClause(resultExpr = resultExpr)) in withCases do
                walkExpr functionNames acc resultExpr
        | SynExpr.TryFinally(tryExpr = tryExpr; finallyExpr = finallyExpr) ->
            walkExpr functionNames acc tryExpr
            walkExpr functionNames acc finallyExpr
        | SynExpr.Record(_, _, fields, _) ->
            for SynExprRecordField(expr = exprOpt) in fields do
                exprOpt |> Option.iter (walkExpr functionNames acc)
        | SynExpr.ObjExpr(bindings = bindings) ->
            for binding in bindings do
                let (SynBinding(expr = bindExpr)) = binding
                walkExpr functionNames acc bindExpr
        | SynExpr.While(_, whileExpr, doExpr, _) ->
            walkExpr functionNames acc whileExpr
            walkExpr functionNames acc doExpr
        | SynExpr.ForEach(_, _, _, _, _, enumExpr, bodyExpr, _) ->
            walkExpr functionNames acc enumExpr
            walkExpr functionNames acc bodyExpr
        | SynExpr.YieldOrReturn(_, expr, _, _) ->
            walkExpr functionNames acc expr
        | SynExpr.YieldOrReturnFrom(_, expr, _, _) ->
            walkExpr functionNames acc expr
        | _ -> ()

    /// Walk module declarations, recursing into nested modules and let bindings.
    let rec private walkDecls (functionNames: Set<string>) (acc: ResizeArray<TypeInfo>) (decls: SynModuleDecl list) =
        for decl in decls do
            match decl with
            | SynModuleDecl.Let(_, bindings, _) ->
                for binding in bindings do
                    let (SynBinding(expr = bindExpr)) = binding
                    walkExpr functionNames acc bindExpr
            | SynModuleDecl.Expr(expr, _) ->
                walkExpr functionNames acc expr
            | SynModuleDecl.NestedModule(decls = nestedDecls) ->
                walkDecls functionNames acc nestedDecls
            | _ -> ()

    /// Extract explicit type arguments from calls to the named functions.
    let extractCallTypeArgs (functionNames: Set<string>) (filePath: string) (sourceText: string) : TypeInfo list =
        let source = SourceText.ofString sourceText

        let parsingOptions =
            { FSharpParsingOptions.Default with
                SourceFiles = [| filePath |] }

        let parseResults =
            checker.ParseFile(filePath, source, parsingOptions)
            |> Async.RunSynchronously

        let acc = ResizeArray<TypeInfo>()

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for SynModuleOrNamespace(decls = decls) in modules do
                walkDecls functionNames acc decls
        | _ -> ()

        Seq.toList acc
