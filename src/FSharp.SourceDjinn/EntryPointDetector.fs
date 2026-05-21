namespace FSharp.SourceDjinn

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type EntryPointInfo =
    { ModuleName : string
      FunctionName : string
      BootstrapInterface : string }

module EntryPointDetector =

    let private checker = FSharpChecker.Create()

    let private normalizeAttrName (name: string) =
        if name.EndsWith("Attribute") then name.Substring(0, name.Length - 9)
        else name

    let private isEntryPointAttr (attr: SynAttribute) =
        let rawName =
            match attr.TypeName with
            | SynLongIdent(id = idents) ->
                idents |> List.map (fun i -> i.idText) |> String.concat "."
        let shortName = normalizeAttrName (match rawName.LastIndexOf('.') with | -1 -> rawName | i -> rawName.Substring(i + 1))
        shortName = "EntryPoint"

    let private tryExtractFunctionName (pat: SynPat) =
        match pat with
        | SynPat.LongIdent(longDotId = SynLongIdent(id = idents)) ->
            idents |> List.tryLast |> Option.map (fun i -> i.idText)
        | SynPat.Named(ident = SynIdent(ident, _)) ->
            Some ident.idText
        | _ -> None

    let private findEntryPointInBindings (bindings: SynBinding list) =
        bindings |> List.tryPick (fun (SynBinding(attributes = attrs; headPat = headPat)) ->
            let hasEntryPoint =
                attrs
                |> List.collect (fun attrList -> attrList.Attributes)
                |> List.exists isEntryPointAttr
            if hasEntryPoint then tryExtractFunctionName headPat
            else None)

    let rec private findEntryPointInDecls (decls: SynModuleDecl list) =
        decls |> List.tryPick (fun decl ->
            match decl with
            | SynModuleDecl.Let(_, bindings, _) ->
                findEntryPointInBindings bindings
            | SynModuleDecl.NestedModule(decls = nestedDecls) ->
                findEntryPointInDecls nestedDecls
            | _ -> None)

    let detect (filePath: string) (sourceText: string) : EntryPointInfo option =
        let source = SourceText.ofString sourceText

        let parsingOptions =
            { FSharpParsingOptions.Default with
                SourceFiles = [| filePath |] }

        let parseResults =
            checker.ParseFile(filePath, source, parsingOptions)
            |> Async.RunSynchronously

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            modules |> List.tryPick (fun (SynModuleOrNamespace(longId = nsId; decls = decls)) ->
                match findEntryPointInDecls decls with
                | Some funcName ->
                    let moduleName = nsId |> List.map (fun i -> i.idText) |> String.concat "."
                    Some { ModuleName = moduleName; FunctionName = funcName; BootstrapInterface = "FSharp.SourceDjinn.TypeModel.IEntryPointBootstrap" }
                | None -> None)
        | _ -> None
