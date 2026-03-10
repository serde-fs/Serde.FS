namespace Serde.FS.SourceGen

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Serde.FS
open FSharp.SourceDjinn.TypeModel.Types

module internal RootGenericDiagnostics =

    type DiagnosticKind =
        | MissingTypeArg
        | WildcardTypeArg

    type CallSiteDiagnostic = {
        Kind: DiagnosticKind
        FunctionName: string
        TypeDisplay: string
        SuggestedFix: string
        RelativePath: string
        Line: int
        Column: int
    }

    let private checker = FSharpChecker.Create()

    let private identToString (idents: LongIdent) =
        idents |> List.map (fun i -> i.idText) |> String.concat "."

    let private serdeNames = set [ "Serde.Serialize"; "Serde.Deserialize" ]

    let private isSerdeCall (lid: SynLongIdent) =
        match lid with
        | SynLongIdent(id = idents) -> serdeNames.Contains(identToString idents)

    let private getSerdeFunc (lid: SynLongIdent) =
        match lid with
        | SynLongIdent(id = idents) ->
            let last = (List.last idents).idText
            if last = "Serialize" || last = "Deserialize" then Some last else None

    /// Get the head identifier of an expression (the constructor/function being applied).
    let rec private getExprHead (expr: SynExpr) =
        match expr with
        | SynExpr.Paren(expr = inner) -> getExprHead inner
        | SynExpr.App(_, _, func, _, _) -> getExprHead func
        | SynExpr.Ident(ident) -> Some ident.idText
        | SynExpr.LongIdent(_, SynLongIdent(id = idents), _, _) -> Some(identToString idents)
        | _ -> None

    /// Try to get the argument name if it's a simple identifier.
    let private tryGetArgName (expr: SynExpr) =
        match expr with
        | SynExpr.Paren(expr = SynExpr.Ident(ident)) -> Some ident.idText
        | SynExpr.Ident(ident) -> Some ident.idText
        | _ -> None

    /// Check if a SynType represents a constructed generic; return (baseName, argNames).
    let private tryGetConstructedGeneric (synType: SynType) =
        match synType with
        | SynType.App(SynType.LongIdent(SynLongIdent(id = idents)), _, typeArgs, _, _, _, _) when not typeArgs.IsEmpty ->
            let baseName = identToString idents
            let args =
                typeArgs
                |> List.map (fun t ->
                    match t with
                    | SynType.LongIdent(SynLongIdent(id = argIdents)) -> identToString argIdents
                    | SynType.Var(SynTypar(ident, _, _), _) -> $"'%s{ident.idText}"
                    | _ -> "_")
            Some(baseName, args)
        | _ -> None

    /// Check if a SynType contains a wildcard `_` type argument.
    let rec private hasWildcardTypeArg (synType: SynType) =
        match synType with
        | SynType.Anon _ -> true
        | SynType.App(_, _, typeArgs, _, _, _, _) ->
            typeArgs |> List.exists hasWildcardTypeArg
        | SynType.Paren(inner, _) -> hasWildcardTypeArg inner
        | _ -> false

    /// Render a SynType for display purposes.
    let rec private synTypeToDisplay (synType: SynType) =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
        | SynType.App(typeName, _, typeArgs, _, _, _, _) ->
            let baseName = synTypeToDisplay typeName
            let args = typeArgs |> List.map synTypeToDisplay |> String.concat ", "
            $"%s{baseName}<%s{args}>"
        | SynType.Anon _ -> "_"
        | SynType.Var(SynTypar(ident, _, _), _) -> $"'%s{ident.idText}"
        | SynType.Paren(inner, _) -> synTypeToDisplay inner
        | _ -> "?"

    let private computeRelativePath (projectDir: string) (filePath: string) =
        let fullPath = System.IO.Path.GetFullPath(filePath)
        let fullProjDir = System.IO.Path.GetFullPath(projectDir).TrimEnd('\\', '/')
        if fullPath.StartsWith(fullProjDir, System.StringComparison.OrdinalIgnoreCase) then
            fullPath.Substring(fullProjDir.Length).TrimStart('\\', '/').Replace('\\', '/')
        else
            filePath.Replace('\\', '/')

    let private buildFqBase (lookup: Map<string, TypeInfo>) (baseName: string) =
        match Map.tryFind baseName lookup with
        | Some ti ->
            [ yield! ti.Namespace |> Option.toList
              yield! ti.EnclosingModules
              yield ti.TypeName ]
            |> String.concat "."
        | None -> baseName

    let private resolveArgName (lookup: Map<string, TypeInfo>) (a: string) =
        if a.StartsWith("'") || a = "_" then a
        else buildFqBase lookup a

    let private buildFqTypeKnownArgs (lookup: Map<string, TypeInfo>) (baseName: string) (args: string list) =
        let fqBase = buildFqBase lookup baseName
        let fqArgs = args |> List.map (resolveArgName lookup)
        let argStr = String.concat ", " fqArgs
        $"%s{fqBase}<%s{argStr}>"

    let private buildFqTypeUnknownArgs (lookup: Map<string, TypeInfo>) (baseName: string) =
        let fqBase = buildFqBase lookup baseName
        let paramNames =
            match Map.tryFind baseName lookup with
            | Some ti when not ti.GenericParameters.IsEmpty ->
                ti.GenericParameters |> List.map (fun p -> $"'%s{p.Name}")
            | _ -> [ "'T" ]
        let displayArgs = String.concat ", " paramNames
        let fixArgs = paramNames |> List.map (fun _ -> "_") |> String.concat ", "
        let displayType = $"%s{fqBase}<%s{displayArgs}>"
        let fixType = $"%s{fqBase}<%s{fixArgs}>"
        (displayType, fixType)

    /// Try to resolve a name against generic def names and case-to-type map.
    let private tryResolveGenericDef (genericDefNames: Set<string>) (caseToTypeName: Map<string, string>) (name: string) =
        let check n =
            if genericDefNames.Contains n then Some n
            else
                match caseToTypeName.TryGetValue n with
                | true, tn when genericDefNames.Contains tn -> Some tn
                | _ -> None
        match check name with
        | Some tn -> Some tn
        | None ->
            let shortName = name.Split('.') |> Array.last
            check shortName

    // ── AST context ──────────────────────────────────────────────────

    type private Ctx = {
        GenericDefNames: Set<string>
        CaseToTypeName: Map<string, string>
        Lookup: Map<string, TypeInfo>
        ProjectDir: string
        FilePath: string
        Diagnostics: ResizeArray<CallSiteDiagnostic>
    }

    /// Extract constructed generic type annotation from a let-binding pattern.
    let rec private extractPatType (pat: SynPat) =
        match pat with
        | SynPat.Typed(_, synType, _) -> tryGetConstructedGeneric synType
        | SynPat.Paren(inner, _) -> extractPatType inner
        | _ -> None

    let rec private walkExpr (ctx: Ctx) (letPatType: (string * string list) option) (expr: SynExpr) =
        // Check for Serde calls
        match expr with
        // WITH explicit TypeApp — check for wildcard `_` type args
        | SynExpr.App(_, _, SynExpr.TypeApp(SynExpr.LongIdent(_, lid, _, _), _, typeArgs, _, _, _, _), argExpr, range)
            when isSerdeCall lid ->
            if typeArgs |> List.exists hasWildcardTypeArg then
                match getSerdeFunc lid with
                | Some funcName ->
                    let typeDisplay = typeArgs |> List.map synTypeToDisplay |> String.concat ", "
                    let argName = tryGetArgName argExpr |> Option.defaultValue "value"
                    let pos = range.Start
                    ctx.Diagnostics.Add({
                        Kind = WildcardTypeArg
                        FunctionName = funcName
                        TypeDisplay = typeDisplay
                        SuggestedFix = $"Serde.%s{funcName}<%s{typeDisplay}>(%s{argName})"
                        RelativePath = computeRelativePath ctx.ProjectDir ctx.FilePath
                        Line = pos.Line
                        Column = pos.Column
                    })
                | None -> ()

        // WITHOUT TypeApp — potential diagnostic
        | SynExpr.App(_, _, SynExpr.LongIdent(_, lid, _, _), argExpr, range)
            when isSerdeCall lid ->
            match getSerdeFunc lid with
            | Some funcName ->
                if funcName = "Serialize" then
                    match getExprHead argExpr with
                    | Some head ->
                        match tryResolveGenericDef ctx.GenericDefNames ctx.CaseToTypeName head with
                        | Some tn ->
                            let (typeDisplay, fixType) = buildFqTypeUnknownArgs ctx.Lookup tn
                            let argName = tryGetArgName argExpr |> Option.defaultValue "value"
                            let suggestedFix = $"Serde.Serialize<%s{fixType}>(%s{argName})"
                            let pos = range.Start
                            ctx.Diagnostics.Add({
                                Kind = MissingTypeArg
                                FunctionName = funcName
                                TypeDisplay = typeDisplay
                                SuggestedFix = suggestedFix
                                RelativePath = computeRelativePath ctx.ProjectDir ctx.FilePath
                                Line = pos.Line
                                Column = pos.Column
                            })
                        | None -> ()
                    | None -> ()

                elif funcName = "Deserialize" then
                    match letPatType with
                    | Some(baseName, args) ->
                        match tryResolveGenericDef ctx.GenericDefNames ctx.CaseToTypeName baseName with
                        | Some tn ->
                            let typeDisplay = buildFqTypeKnownArgs ctx.Lookup tn args
                            let suggestedFix = $"Serde.Deserialize<%s{typeDisplay}>(json)"
                            let pos = range.Start
                            ctx.Diagnostics.Add({
                                Kind = MissingTypeArg
                                FunctionName = funcName
                                TypeDisplay = typeDisplay
                                SuggestedFix = suggestedFix
                                RelativePath = computeRelativePath ctx.ProjectDir ctx.FilePath
                                Line = pos.Line
                                Column = pos.Column
                            })
                        | None -> ()
                    | None -> ()
            | None -> ()

        | _ -> ()

        // Recurse into child expressions
        match expr with
        | SynExpr.App(_, _, funcExpr, argExpr, _) ->
            walkExpr ctx letPatType funcExpr
            walkExpr ctx letPatType argExpr
        | SynExpr.LetOrUse(bindings = bindings; body = body) ->
            for binding in bindings do
                walkBinding ctx binding
            walkExpr ctx None body
        | SynExpr.Sequential(_, _, e1, e2, _, _) ->
            walkExpr ctx letPatType e1
            walkExpr ctx letPatType e2
        | SynExpr.IfThenElse(ifExpr = ie; thenExpr = te; elseExpr = ee) ->
            walkExpr ctx None ie
            walkExpr ctx None te
            ee |> Option.iter (walkExpr ctx None)
        | SynExpr.Match(expr = e; clauses = clauses)
        | SynExpr.MatchBang(expr = e; clauses = clauses) ->
            walkExpr ctx None e
            for SynMatchClause(resultExpr = re) in clauses do
                walkExpr ctx None re
        | SynExpr.Lambda(body = b) ->
            walkExpr ctx None b
        | SynExpr.Paren(expr = inner) ->
            walkExpr ctx letPatType inner
        | SynExpr.Tuple(_, exprs, _, _) ->
            for e in exprs do walkExpr ctx None e
        | SynExpr.Do(e, _) ->
            walkExpr ctx None e
        | SynExpr.TryWith(tryExpr = te; withCases = wc) ->
            walkExpr ctx None te
            for SynMatchClause(resultExpr = re) in wc do
                walkExpr ctx None re
        | SynExpr.TryFinally(tryExpr = te; finallyExpr = fe) ->
            walkExpr ctx None te
            walkExpr ctx None fe
        | _ -> ()

    and private walkBinding (ctx: Ctx) (binding: SynBinding) =
        let (SynBinding(headPat = pat; expr = bindExpr)) = binding
        let patType = extractPatType pat
        walkExpr ctx patType bindExpr

    let rec private walkDecls (ctx: Ctx) (decls: SynModuleDecl list) =
        for decl in decls do
            match decl with
            | SynModuleDecl.Let(_, bindings, _) ->
                for binding in bindings do
                    walkBinding ctx binding
            | SynModuleDecl.Expr(expr, _) ->
                walkExpr ctx None expr
            | SynModuleDecl.NestedModule(decls = nestedDecls) ->
                walkDecls ctx nestedDecls
            | _ -> ()

    // ── Public API ───────────────────────────────────────────────────

    let detect
        (genericDefs: SerdeTypeInfo seq)
        (lookup: Map<string, TypeInfo>)
        (projectDir: string)
        (filePath: string)
        (sourceText: string) : CallSiteDiagnostic list =

        let genericDefNames =
            genericDefs
            |> Seq.filter (fun t -> t.Raw.IsGenericDefinition)
            |> Seq.map (fun t -> t.Raw.TypeName)
            |> Set.ofSeq

        let caseToTypeName =
            genericDefs
            |> Seq.filter (fun t -> t.Raw.IsGenericDefinition)
            |> Seq.collect (fun t ->
                match t.UnionCases with
                | Some cases -> cases |> List.map (fun c -> c.CaseName, t.Raw.TypeName)
                | None -> [])
            |> Map.ofSeq

        if genericDefNames.IsEmpty then [] else

        let source = SourceText.ofString sourceText
        let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| filePath |] }
        let parseResults = checker.ParseFile(filePath, source, parsingOptions) |> Async.RunSynchronously

        let ctx = {
            GenericDefNames = genericDefNames
            CaseToTypeName = caseToTypeName
            Lookup = lookup
            ProjectDir = projectDir
            FilePath = filePath
            Diagnostics = ResizeArray()
        }

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for SynModuleOrNamespace(decls = decls) in modules do
                walkDecls ctx decls
        | _ -> ()

        Seq.toList ctx.Diagnostics

    let formatMessage (d: CallSiteDiagnostic) =
        let nl = System.Environment.NewLine
        match d.Kind with
        | MissingTypeArg ->
            let verb =
                if d.FunctionName = "Serialize" then "The value passed to"
                else "The return type of"
            $"%s{verb} `%s{d.FunctionName}` has a constructed generic type: %s{d.TypeDisplay}  --  at: %s{d.RelativePath}(%d{d.Line},%d{d.Column})%s{nl}%s{nl}Root-level constructed generics require an explicit type argument. Use: %s{d.SuggestedFix}"
        | WildcardTypeArg ->
            $"`Serde.%s{d.FunctionName}<%s{d.TypeDisplay}>` contains a wildcard type argument `_`.  --  at: %s{d.RelativePath}(%d{d.Line},%d{d.Column})%s{nl}%s{nl}Serde requires fully specified type arguments — replace `_` with the concrete type."
