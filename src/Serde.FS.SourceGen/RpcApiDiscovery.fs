namespace Serde.FS.SourceGen

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS

/// Discovers types transitively referenced from [<RpcApi>] interfaces.
/// Uses FSharpChecker directly because SourceDjinn does not parse interfaces.
module internal RpcApiDiscovery =

    let private checker = FSharpChecker.Create()

    let private identToString (idents: LongIdent) =
        idents |> List.map (fun i -> i.idText) |> String.concat "."

    let private rpcApiAttrNames = set [ "RpcApi"; "RpcApiAttribute" ]
    let private generateFableClientAttrNames = set [ "GenerateFableClient"; "GenerateFableClientAttribute" ]

    /// Names of types to skip during closure computation (primitives, wrappers, collections).
    let private skipTypeNames =
        set [
            "unit"; "bool"; "string"; "int"; "int8"; "int16"; "int32"; "int64"
            "uint8"; "uint16"; "uint32"; "uint64"; "byte"
            "float"; "float32"; "double"; "single"; "decimal"
            "sbyte"
            "Guid"; "System.Guid"
            "DateTime"; "System.DateTime"
            "DateTimeOffset"; "System.DateTimeOffset"
            "TimeSpan"; "System.TimeSpan"
            "DateOnly"; "System.DateOnly"
            "TimeOnly"; "System.TimeOnly"
            "Async"; "Task"; "System.Threading.Tasks.Task"
            "option"; "Option"
            "list"; "List"
            "array"; "Array"
            "Set"; "Map"
            "seq"; "Seq"
            "Result"
        ]

    /// Async/Task wrapper names to unwrap for return types.
    let private asyncWrapperNames = set [ "Async"; "Task" ]

    /// Recursively extract all type names referenced in a SynType.
    /// Unwraps Async<T>, Task<T>, and function types (A -> B).
    let rec private collectTypeNames (synType: SynType) : string list =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            if skipTypeNames.Contains name then []
            else [ name ]

        | SynType.App(typeName, _, typeArgs, _, _, _, _) ->
            let baseName =
                match typeName with
                | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
                | _ -> ""
            // If this is Async<T>, Task<T>, option<T>, list<T>, etc. — unwrap and collect from args only
            if skipTypeNames.Contains baseName then
                typeArgs |> List.collect collectTypeNames
            else
                // User-defined generic type — collect the base name + args
                [ baseName ] @ (typeArgs |> List.collect collectTypeNames)

        | SynType.Fun(argType, returnType, _, _) ->
            collectTypeNames argType @ collectTypeNames returnType

        | SynType.Tuple(_, segments, _) ->
            segments
            |> List.collect (fun seg ->
                match seg with
                | SynTupleTypeSegment.Type t -> collectTypeNames t
                | _ -> [])

        | SynType.Paren(innerType, _) ->
            collectTypeNames innerType

        | SynType.Array(_, elementType, _) ->
            collectTypeNames elementType

        | SynType.Var _ -> [] // generic parameter like 'T — skip
        | _ -> []

    /// Render a SynType as an F# type expression string.
    /// The resolve function maps short type names to fully qualified names.
    let rec private synTypeToString (resolve: string -> string) (synType: SynType) : string =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            resolve name

        | SynType.App(typeName, _, typeArgs, _, _, isPostfix, _) ->
            let baseName = synTypeToString resolve typeName
            if typeArgs.IsEmpty then baseName
            elif isPostfix && typeArgs.Length = 1 then
                $"{synTypeToString resolve typeArgs.[0]} {baseName}"
            else
                let args = typeArgs |> List.map (synTypeToString resolve) |> String.concat ", "
                $"{baseName}<{args}>"

        | SynType.Fun(argType, returnType, _, _) ->
            $"{synTypeToString resolve argType} -> {synTypeToString resolve returnType}"

        | SynType.Tuple(_, segments, _) ->
            segments
            |> List.choose (fun seg ->
                match seg with
                | SynTupleTypeSegment.Type t -> Some (synTypeToString resolve t)
                | _ -> None)
            |> String.concat " * "

        | SynType.Paren(innerType, _) ->
            synTypeToString resolve innerType

        | SynType.Array(rank, elementType, _) ->
            let suffix = System.String(',', rank - 1)
            $"{synTypeToString resolve elementType}[{suffix}]"

        | SynType.Var(SynTypar(ident, _, _), _) ->
            $"'{ident.idText}"

        | _ -> "obj"

    /// Unwrap Async<T> or Task<T> and return the inner type string.
    let rec private unwrapAsyncType (resolve: string -> string) (synType: SynType) : string =
        match synType with
        | SynType.App(typeName, _, [ innerType ], _, _, _, _) ->
            let baseName =
                match typeName with
                | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
                | _ -> ""
            if asyncWrapperNames.Contains baseName then
                synTypeToString resolve innerType
            else
                synTypeToString resolve synType
        | SynType.Paren(innerType, _) ->
            unwrapAsyncType resolve innerType
        | _ ->
            synTypeToString resolve synType

    /// Extract the input type and return type from a method signature SynType.
    /// For `A -> Async<B>`, returns (inputType="A", outputType="B").
    let private extractMethodTypes (resolve: string -> string) (synType: SynType) : string * string =
        match synType with
        | SynType.Fun(argType, returnType, _, _) ->
            let inputStr = synTypeToString resolve argType
            let outputStr = unwrapAsyncType resolve returnType
            (inputStr, outputStr)
        | _ ->
            ("unit", synTypeToString resolve synType)

    /// Extract method name and types from an abstract member's SynValSig.
    let private extractMethodInfo (resolve: string -> string) (valSig: SynValSig) : RpcMethodInfo option =
        let (SynValSig(ident = SynIdent(ident, _); synType = synType)) = valSig
        let inputType, outputType = extractMethodTypes resolve synType
        Some {
            MethodName = ident.idText
            InputType = inputType
            OutputType = outputType
        }

    /// Extract type names from an abstract member's SynValSig.
    let private extractFromValSig (valSig: SynValSig) : string list =
        let (SynValSig(synType = synType)) = valSig
        collectTypeNames synType

    /// Parsed [<RpcApi>] attribute properties.
    type private RpcApiAttrProps = {
        Root: string option
        Version: string option
        UrlCaseValue: int
    }

    /// Parsed [<GenerateFableClient>] attribute properties.
    type private FableClientAttrProps = {
        OutputDir: string option
    }

    /// Try to extract a string constant from a SynExpr.
    let private tryGetStringConst (expr: SynExpr) =
        match expr with
        | SynExpr.Const(SynConst.String(s, _, _), _) -> Some s
        | _ -> None

    /// Try to extract an int constant from a SynExpr (for enum values).
    let private tryGetIntConst (expr: SynExpr) =
        match expr with
        | SynExpr.Const(SynConst.Int32 i, _) -> Some i
        | _ -> None

    /// Try to extract a named arg value from a SynExpr like `Name = value` or `Name = Enum.Case`.
    let private tryExtractNamedArg (expr: SynExpr) : (string * SynExpr) option =
        match expr with
        | SynExpr.App(_, _,
            SynExpr.App(_, true, _, SynExpr.Ident(nameIdent), _),
            valueExpr, _) ->
                Some (nameIdent.idText, valueExpr)
        | _ -> None

    /// Resolve a UrlCase enum reference like `UrlCase.Kebab` to its int value.
    let private resolveUrlCaseValue (expr: SynExpr) : int =
        match expr with
        | SynExpr.LongIdent(_, SynLongIdent(id = idents), _, _) ->
            let name = idents |> List.last |> fun i -> i.idText
            match name with
            | "Kebab" -> 1
            | _ -> 0  // Default
        | SynExpr.Ident(ident) ->
            match ident.idText with
            | "Kebab" -> 1
            | _ -> 0
        | _ ->
            match tryGetIntConst expr with
            | Some i -> i
            | None -> 0

    /// Try to find and parse the [<RpcApi>] attribute from a SynComponentInfo.
    /// Returns None if the attribute is not present.
    let private tryGetRpcApiAttr (synComponentInfo: SynComponentInfo) : RpcApiAttrProps option =
        let (SynComponentInfo(attributes = attrs)) = synComponentInfo
        let rpcApiAttr =
            attrs
            |> List.tryPick (fun attrList ->
                attrList.Attributes
                |> List.tryFind (fun attr ->
                    match attr.TypeName with
                    | SynLongIdent(id = idents) ->
                        let name = identToString idents
                        rpcApiAttrNames.Contains name))
        match rpcApiAttr with
        | None -> None
        | Some attr ->
            let mutable root = None
            let mutable version = None
            let mutable urlCaseValue = 0

            // Parse named args from the attribute constructor
            let parseArgs (argExpr: SynExpr) =
                let processArg expr =
                    match tryExtractNamedArg expr with
                    | Some ("Root", valExpr) -> root <- tryGetStringConst valExpr
                    | Some ("Version", valExpr) -> version <- tryGetStringConst valExpr
                    | Some ("UrlCase", valExpr) -> urlCaseValue <- resolveUrlCaseValue valExpr
                    | _ -> ()

                match argExpr with
                | SynExpr.Const(SynConst.Unit, _) -> ()
                | SynExpr.Paren(SynExpr.Const(SynConst.Unit, _), _, _, _) -> ()
                | SynExpr.Paren(SynExpr.Tuple(_, exprs, _, _), _, _, _) ->
                    for expr in exprs do processArg expr
                | SynExpr.Paren(inner, _, _, _) -> processArg inner
                | other -> processArg other

            parseArgs attr.ArgExpr
            Some { Root = root; Version = version; UrlCaseValue = urlCaseValue }

    /// Try to find and parse the [<GenerateFableClient>] attribute from a SynComponentInfo.
    /// Returns None if the attribute is not present.
    let private tryGetFableClientAttr (synComponentInfo: SynComponentInfo) : FableClientAttrProps option =
        let (SynComponentInfo(attributes = attrs)) = synComponentInfo
        let fableAttr =
            attrs
            |> List.tryPick (fun attrList ->
                attrList.Attributes
                |> List.tryFind (fun attr ->
                    match attr.TypeName with
                    | SynLongIdent(id = idents) ->
                        let name = identToString idents
                        generateFableClientAttrNames.Contains name))
        match fableAttr with
        | None -> None
        | Some attr ->
            let mutable outputDir = None

            let parseArgs (argExpr: SynExpr) =
                let processArg expr =
                    match tryExtractNamedArg expr with
                    | Some ("OutputDir", valExpr) -> outputDir <- tryGetStringConst valExpr
                    | _ -> ()

                match argExpr with
                | SynExpr.Const(SynConst.Unit, _) -> ()
                | SynExpr.Paren(SynExpr.Const(SynConst.Unit, _), _, _, _) -> ()
                | SynExpr.Paren(SynExpr.Tuple(_, exprs, _, _), _, _, _) ->
                    for expr in exprs do processArg expr
                | SynExpr.Paren(inner, _, _, _) -> processArg inner
                | other -> processArg other

            parseArgs attr.ArgExpr
            Some { OutputDir = outputDir }

    /// Get the fully qualified name from a SynComponentInfo in the context of a namespace/modules.
    let private getTypeName (ns: string option) (modules: string list) (synComponentInfo: SynComponentInfo) : string * string =
        let (SynComponentInfo(longId = typeNameIdent)) = synComponentInfo
        let shortName = typeNameIdent |> List.map (fun i -> i.idText) |> String.concat "."
        let parts = [ yield! ns |> Option.toList; yield! modules; yield shortName ]
        let fullName = String.concat "." parts
        (fullName, shortName)

    /// Collected data from walking [<RpcApi>] interfaces.
    type private RpcApiCollected = {
        TypeNames: ResizeArray<string>
        Interfaces: ResizeArray<RpcInterfaceInfo>
    }

    /// Walk a parsed AST to find [<RpcApi>] interfaces and extract type names + method info.
    let private findRpcApis (resolve: string -> string) (filePath: string) (sourceText: string) : RpcApiCollected =
        let source = SourceText.ofString sourceText
        let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| filePath |] }
        let parseResults = checker.ParseFile(filePath, source, parsingOptions) |> Async.RunSynchronously

        let collected = {
            TypeNames = ResizeArray<string>()
            Interfaces = ResizeArray<RpcInterfaceInfo>()
        }

        let walkTypeDefn (ns: string option) (modules: string list) (typeDefn: SynTypeDefn) =
            let (SynTypeDefn(typeInfo = synComponentInfo; typeRepr = typeRepr; members = members)) = typeDefn
            match tryGetRpcApiAttr synComponentInfo with
            | Some attrProps ->
                let fullName, shortName = getTypeName ns modules synComponentInfo
                let methods = ResizeArray<RpcMethodInfo>()

                // Extract from ObjectModel representation (interface members)
                match typeRepr with
                | SynTypeDefnRepr.ObjectModel(_, memberDefns, _) ->
                    for memberDefn in memberDefns do
                        match memberDefn with
                        | SynMemberDefn.AbstractSlot(slotSig, _, _, _) ->
                            collected.TypeNames.AddRange(extractFromValSig slotSig)
                            match extractMethodInfo resolve slotSig with
                            | Some mi -> methods.Add(mi)
                            | None -> ()
                        | _ -> ()
                | _ -> ()
                // Also check augmentation members
                for memberDefn in members do
                    match memberDefn with
                    | SynMemberDefn.AbstractSlot(slotSig, _, _, _) ->
                        collected.TypeNames.AddRange(extractFromValSig slotSig)
                        match extractMethodInfo resolve slotSig with
                        | Some mi -> methods.Add(mi)
                        | None -> ()
                    | _ -> ()

                let fableProps = tryGetFableClientAttr synComponentInfo
                collected.Interfaces.Add({
                    FullName = fullName
                    ShortName = shortName
                    Methods = Seq.toList methods
                    Root = attrProps.Root
                    Version = attrProps.Version
                    UrlCaseValue = attrProps.UrlCaseValue
                    GenerateFableClient = fableProps.IsSome
                    FableOutputDir = fableProps |> Option.bind (fun p -> p.OutputDir)
                    SourceFilePath = Some filePath
                })
            | None -> ()

        let rec walkDecls (ns: string option) (modules: string list) (decls: SynModuleDecl list) =
            for decl in decls do
                match decl with
                | SynModuleDecl.Types(typeDefns, _) ->
                    for td in typeDefns do walkTypeDefn ns modules td
                | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = moduleIdent); decls = nestedDecls) ->
                    let moduleName = moduleIdent |> List.map (fun i -> i.idText) |> String.concat "."
                    walkDecls ns (modules @ [moduleName]) nestedDecls
                | _ -> ()

        match parseResults.ParseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for SynModuleOrNamespace(longId = nsId; kind = kind; decls = decls) in modules do
                match kind with
                | SynModuleOrNamespaceKind.DeclaredNamespace ->
                    let ns = Some(nsId |> List.map (fun i -> i.idText) |> String.concat ".")
                    walkDecls ns [] decls
                | SynModuleOrNamespaceKind.NamedModule ->
                    let moduleNames = nsId |> List.map (fun i -> i.idText)
                    walkDecls None moduleNames decls
                | _ ->
                    walkDecls None [] decls
        | _ -> ()

        collected

    /// Build a lookup map from short type name to TypeInfo.
    let private buildLookup (allTypeInfos: TypeInfo list) : Map<string, TypeInfo> =
        allTypeInfos
        |> List.map (fun ti -> ti.TypeName, ti)
        |> Map.ofList

    /// Recursively collect all type names from a TypeInfo's fields and union cases.
    let rec private collectTransitiveTypeNames (lookup: Map<string, TypeInfo>) (visited: Set<string>) (typeName: string) : Set<string> =
        if visited.Contains typeName then visited
        else
            match Map.tryFind typeName lookup with
            | None -> visited
            | Some ti ->
                let visited = visited.Add typeName
                let fieldTypes =
                    match ti.Kind with
                    | Record fields | AnonymousRecord fields ->
                        fields |> List.collect (fun f -> extractTypeNamesFromTypeInfo f.Type)
                    | Union cases ->
                        cases |> List.collect (fun c -> c.Fields |> List.collect (fun f -> extractTypeNamesFromTypeInfo f.Type))
                    | _ -> []
                fieldTypes
                |> List.filter (fun n -> not (skipTypeNames.Contains n))
                |> List.fold (fun acc n -> collectTransitiveTypeNames lookup acc n) visited

    /// Extract type names from a SourceDjinn TypeInfo, unwrapping collections/options.
    and private extractTypeNamesFromTypeInfo (ti: TypeInfo) : string list =
        match ti.Kind with
        | Primitive _ | GenericParameter _ | GenericTypeDefinition _ -> []
        | Record _ | Union _ | Enum _ | AnonymousRecord _ ->
            if skipTypeNames.Contains ti.TypeName then []
            else [ ti.TypeName ]
        | Option inner | List inner | Array inner | Set inner ->
            extractTypeNamesFromTypeInfo inner
        | Map (k, v) ->
            extractTypeNamesFromTypeInfo k @ extractTypeNamesFromTypeInfo v
        | Tuple fields ->
            fields |> List.collect (fun f -> extractTypeNamesFromTypeInfo f.Type)
        | ConstructedGenericType ->
            let baseName =
                if skipTypeNames.Contains ti.TypeName then []
                else [ ti.TypeName ]
            baseName @ (ti.GenericArguments |> List.collect extractTypeNamesFromTypeInfo)

    /// Resolve a short type name to its fully qualified name using the lookup map.
    let private resolveTypeName (lookup: Map<string, TypeInfo>) (name: string) : string =
        match Map.tryFind name lookup with
        | Some ti ->
            let parts =
                [ yield! ti.Namespace |> Option.toList
                  yield! ti.EnclosingModules
                  yield ti.TypeName ]
            String.concat "." parts
        | None -> name

    /// Discover all types transitively referenced from [<RpcApi>] interfaces.
    /// Returns both discovered types (for codec generation) and interface metadata (for RPC dispatch modules).
    let discover (allTypeInfos: TypeInfo list) (sourceFiles: (string * string) list) : RpcDiscoveryResult =
        // Build lookup early so we can resolve type names in method signatures
        let lookup = buildLookup allTypeInfos
        let resolve = resolveTypeName lookup

        // Step 1: Find all [<RpcApi>] interfaces and collect type names + method info
        let allCollected =
            sourceFiles
            |> List.collect (fun (filePath, sourceText) ->
                if filePath.EndsWith(".fs") then
                    try [ findRpcApis resolve filePath sourceText ]
                    with _ -> []
                else [])

        let rootTypeNames =
            allCollected
            |> List.collect (fun c -> Seq.toList c.TypeNames)
            |> List.distinct

        let interfaces =
            allCollected
            |> List.collect (fun c -> Seq.toList c.Interfaces)

        if rootTypeNames.IsEmpty then
            { DiscoveredTypes = []; Interfaces = interfaces }
        else
            // Step 2: Compute transitive closure
            let allDiscoveredNames =
                rootTypeNames
                |> List.filter (fun n -> not (skipTypeNames.Contains n))
                |> List.fold (fun acc name -> collectTransitiveTypeNames lookup acc name) Set.empty

            // Step 3: Build SerdeTypeInfo for each discovered type
            let discoveredTypes =
                allDiscoveredNames
                |> Set.toList
                |> List.choose (fun name -> Map.tryFind name lookup)
                |> List.filter (fun ti ->
                    match ti.Kind with
                    | Record _ | Union _ | Enum _ | AnonymousRecord _ -> true
                    | _ -> false)
                |> List.map SerdeMetadataBuilder.buildSerdeTypeInfo

            { DiscoveredTypes = discoveredTypes; Interfaces = interfaces }
