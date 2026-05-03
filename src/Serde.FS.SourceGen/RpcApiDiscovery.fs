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

    /// Recursively expand type abbreviations in a SynType. For each LongIdent
    /// whose name matches an alias key, replace it with the (recursively expanded)
    /// target SynType. This lets `type PageSize = int` resolve correctly when used
    /// in interface signatures.
    let rec private expandAliases (aliases: Map<string, SynType>) (synType: SynType) : SynType =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            let shortName = idents |> List.last |> fun i -> i.idText
            let target =
                match Map.tryFind name aliases with
                | Some t -> Some t
                | None -> Map.tryFind shortName aliases
            match target with
            | Some t -> expandAliases aliases t
            | None -> synType
        | SynType.App(typeName, lt, args, commas, gt, isPostfix, range) ->
            SynType.App(
                expandAliases aliases typeName,
                lt,
                args |> List.map (expandAliases aliases),
                commas,
                gt,
                isPostfix,
                range)
        | SynType.Fun(arg, ret, range, trivia) ->
            SynType.Fun(expandAliases aliases arg, expandAliases aliases ret, range, trivia)
        | SynType.Tuple(isStruct, segments, range) ->
            let segments' =
                segments
                |> List.map (fun seg ->
                    match seg with
                    | SynTupleTypeSegment.Type t -> SynTupleTypeSegment.Type (expandAliases aliases t)
                    | other -> other)
            SynType.Tuple(isStruct, segments', range)
        | SynType.Paren(inner, range) ->
            SynType.Paren(expandAliases aliases inner, range)
        | SynType.Array(rank, elem, range) ->
            SynType.Array(rank, expandAliases aliases elem, range)
        | other -> other

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

    /// Extract the input type, multi-arg flag, per-param types, and return type from a method signature SynType.
    /// For `A -> Async<B>`, returns (inputType="A", isTupled=false, inputParams=[], outputType="B").
    /// For `A * B -> Async<C>` (multi-arg), returns (inputType="A * B", isTupled=true, inputParams=["A"; "B"], outputType="C").
    /// For `(A * B) -> Async<C>` (single tuple arg), returns (inputType="A * B", isTupled=false, inputParams=[], outputType="C").
    let private extractMethodTypes (resolve: string -> string) (synType: SynType) : string * bool * string list * string =
        match synType with
        | SynType.Fun(argType, returnType, _, _) ->
            let outputStr = unwrapAsyncType resolve returnType
            // Detect multi-arg: a top-level tuple (NOT wrapped in parens). F# treats
            // `abstract Foo: A * B -> C` as a 2-arg method, but `(A * B) -> C` as a 1-arg
            // method taking a tuple.
            match argType with
            | SynType.Tuple(_, segments, _) ->
                let paramTypes =
                    segments
                    |> List.choose (fun seg ->
                        match seg with
                        | SynTupleTypeSegment.Type t -> Some (synTypeToString resolve t)
                        | _ -> None)
                let inputStr = paramTypes |> String.concat " * "
                (inputStr, true, paramTypes, outputStr)
            | _ ->
                let inputStr = synTypeToString resolve argType
                (inputStr, false, [], outputStr)
        | _ ->
            ("unit", false, [], synTypeToString resolve synType)

    /// Extract method name and types from an abstract member's SynValSig.
    /// Aliases are expanded before extracting type strings so `type PageSize = int`
    /// resolves to `int` in method signatures.
    let private extractMethodInfo (resolve: string -> string) (aliases: Map<string, SynType>) (valSig: SynValSig) : RpcMethodInfo option =
        let (SynValSig(ident = SynIdent(ident, _); synType = synType)) = valSig
        let expanded = expandAliases aliases synType
        let inputType, isTupled, inputParams, outputType = extractMethodTypes resolve expanded
        Some {
            MethodName = ident.idText
            InputType = inputType
            InputIsTupled = isTupled
            InputParams = inputParams
            OutputType = outputType
        }

    /// Extract type names from an abstract member's SynValSig (after alias expansion).
    let private extractFromValSig (aliases: Map<string, SynType>) (valSig: SynValSig) : string list =
        let (SynValSig(synType = synType)) = valSig
        let expanded = expandAliases aliases synType
        collectTypeNames expanded

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

    /// Map a primitive name to its PrimitiveKind, or None if not a primitive.
    let private primitiveKindOf (name: string) : PrimitiveKind option =
        match name with
        | "unit" -> Some Unit
        | "bool" | "Boolean" | "System.Boolean" -> Some Bool
        | "sbyte" | "int8" | "SByte" | "System.SByte" -> Some Int8
        | "int16" | "Int16" | "System.Int16" -> Some Int16
        | "int" | "int32" | "Int32" | "System.Int32" -> Some Int32
        | "int64" | "Int64" | "System.Int64" -> Some Int64
        | "byte" | "uint8" | "Byte" | "System.Byte" -> Some UInt8
        | "uint16" | "UInt16" | "System.UInt16" -> Some UInt16
        | "uint32" | "UInt32" | "System.UInt32" -> Some UInt32
        | "uint64" | "UInt64" | "System.UInt64" -> Some UInt64
        | "float32" | "single" | "Single" | "System.Single" -> Some Float32
        | "float" | "double" | "Double" | "System.Double" -> Some Float64
        | "decimal" | "Decimal" | "System.Decimal" -> Some PrimitiveKind.Decimal
        | "string" | "String" | "System.String" -> Some String
        | "Guid" | "System.Guid" -> Some Guid
        | "DateTime" | "System.DateTime" -> Some DateTime
        | "DateTimeOffset" | "System.DateTimeOffset" -> Some DateTimeOffset
        | "TimeSpan" | "System.TimeSpan" -> Some TimeSpan
        | "DateOnly" | "System.DateOnly" -> Some DateOnly
        | "TimeOnly" | "System.TimeOnly" -> Some TimeOnly
        | _ -> None

    let private mkPrimitiveTypeInfo (name: string) (kind: PrimitiveKind) : TypeInfo =
        { Namespace = None
          EnclosingModules = []
          TypeName = name
          Kind = Primitive kind
          Attributes = []
          GenericParameters = []
          GenericArguments = [] }

    /// Convert a SynType to a TypeInfo where possible. Used to surface tuple types
    /// that appear in RPC method signatures so the generator can emit codecs for them.
    /// Returns None for unrecognised shapes.
    let rec private synTypeToTypeInfo (resolveTI: string -> TypeInfo option) (synType: SynType) : TypeInfo option =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            let shortName = idents |> List.last |> fun i -> i.idText
            match primitiveKindOf shortName with
            | Some pk -> Some (mkPrimitiveTypeInfo shortName pk)
            | None -> resolveTI name
        | SynType.Tuple(_, segments, _) ->
            // Tuple segments interleave Type entries with Star separators; only Type
            // entries carry a SynType. We need every Type entry to resolve.
            let typeSegments =
                segments
                |> List.choose (fun seg ->
                    match seg with
                    | SynTupleTypeSegment.Type t -> Some t
                    | _ -> None)
            let elemTis = typeSegments |> List.choose (synTypeToTypeInfo resolveTI)
            if elemTis.Length = typeSegments.Length && elemTis.Length >= 2 then
                let fields =
                    elemTis
                    |> List.mapi (fun i ti ->
                        { Name = sprintf "Item%d" (i + 1); Type = ti; Attributes = [] } : FieldInfo)
                Some { Namespace = None
                       EnclosingModules = []
                       TypeName = "tuple"
                       Kind = Tuple fields
                       Attributes = []
                       GenericParameters = []
                       GenericArguments = [] }
            else None
        | SynType.Paren(inner, _) ->
            synTypeToTypeInfo resolveTI inner
        | SynType.Array(rank, elementType, _) ->
            synTypeToTypeInfo resolveTI elementType
            |> Option.map (fun inner ->
                { Namespace = None
                  EnclosingModules = []
                  TypeName = "array"
                  Kind = Array inner
                  Attributes = []
                  GenericParameters = []
                  GenericArguments = [] })
        | _ -> None

    /// Recursively walk a TypeInfo and collect any Tuple TypeInfos it contains
    /// (including nested ones inside Option/List/etc.).
    let rec private collectTuples (ti: TypeInfo) (acc: TypeInfo list) : TypeInfo list =
        match ti.Kind with
        | Tuple fields ->
            let acc = fields |> List.fold (fun a f -> collectTuples f.Type a) acc
            ti :: acc
        | Option inner | List inner | Array inner | Set inner ->
            collectTuples inner acc
        | Map (k, v) ->
            collectTuples v (collectTuples k acc)
        | _ -> acc

    /// Collected data from walking [<RpcApi>] interfaces.
    type private RpcApiCollected = {
        TypeNames: ResizeArray<string>
        Interfaces: ResizeArray<RpcInterfaceInfo>
        /// Tuple TypeInfos discovered in method input/output signatures.
        /// These need codec generation since they aren't part of any record's fields.
        TupleTypes: ResizeArray<TypeInfo>
    }

    /// Parse a single source file into an AST.
    let private parseFile (filePath: string) (sourceText: string) : ParsedInput option =
        try
            let source = SourceText.ofString sourceText
            let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| filePath |] }
            let parseResults = checker.ParseFile(filePath, source, parsingOptions) |> Async.RunSynchronously
            Some parseResults.ParseTree
        with _ -> None

    /// Walk a ParsedInput and collect type abbreviations as a name → SynType map.
    let private collectAliasesFromAst (parseTree: ParsedInput) : Map<string, SynType> =
        let mutable acc : Map<string, SynType> = Map.empty

        let walkTypeDefn (typeDefn: SynTypeDefn) =
            let (SynTypeDefn(typeInfo = synComponentInfo; typeRepr = typeRepr)) = typeDefn
            let (SynComponentInfo(longId = idents)) = synComponentInfo
            let shortName = idents |> List.last |> fun i -> i.idText
            match typeRepr with
            | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.TypeAbbrev(_, target, _), _) ->
                acc <- Map.add shortName target acc
            | _ -> ()

        let rec walkDecls (decls: SynModuleDecl list) =
            for decl in decls do
                match decl with
                | SynModuleDecl.Types(typeDefns, _) ->
                    for td in typeDefns do walkTypeDefn td
                | SynModuleDecl.NestedModule(decls = nestedDecls) ->
                    walkDecls nestedDecls
                | _ -> ()

        match parseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for SynModuleOrNamespace(decls = decls) in modules do
                walkDecls decls
        | _ -> ()

        acc

    /// Walk a parsed AST to find [<RpcApi>] interfaces and extract type names + method info.
    let private findRpcApis (resolve: string -> string) (aliases: Map<string, SynType>) (resolveTI: string -> TypeInfo option) (filePath: string) (parseTree: ParsedInput) : RpcApiCollected =
        let collected = {
            TypeNames = ResizeArray<string>()
            Interfaces = ResizeArray<RpcInterfaceInfo>()
            TupleTypes = ResizeArray<TypeInfo>()
        }

        let processAbstractSlot (methods: ResizeArray<RpcMethodInfo>) (slotSig: SynValSig) =
            collected.TypeNames.AddRange(extractFromValSig aliases slotSig)
            match extractMethodInfo resolve aliases slotSig with
            | Some mi -> methods.Add(mi)
            | None -> ()
            // Surface tuple types from input/output signatures so codecs get emitted.
            let (SynValSig(synType = synType)) = slotSig
            let expanded = expandAliases aliases synType
            let walkType ti =
                for tup in collectTuples ti [] do
                    collected.TupleTypes.Add(tup)
            // Walk function arrows to extract input/return shapes individually.
            let rec walkSig st =
                match st with
                | SynType.Fun(arg, ret, _, _) ->
                    walkSig arg
                    walkSig ret
                | _ ->
                    match synTypeToTypeInfo resolveTI st with
                    | Some ti -> walkType ti
                    | None -> ()
            walkSig expanded

        let walkTypeDefn (ns: string option) (modules: string list) (isParentNamespace: bool) (typeDefn: SynTypeDefn) =
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
                            processAbstractSlot methods slotSig
                        | _ -> ()
                | _ -> ()
                // Also check augmentation members
                for memberDefn in members do
                    match memberDefn with
                    | SynMemberDefn.AbstractSlot(slotSig, _, _, _) ->
                        processAbstractSlot methods slotSig
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
                    IsParentNamespace = isParentNamespace
                })
            | None -> ()

        let rec walkDecls (ns: string option) (modules: string list) (isParentNamespace: bool) (decls: SynModuleDecl list) =
            for decl in decls do
                match decl with
                | SynModuleDecl.Types(typeDefns, _) ->
                    for td in typeDefns do walkTypeDefn ns modules isParentNamespace td
                | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = moduleIdent); decls = nestedDecls) ->
                    let moduleName = moduleIdent |> List.map (fun i -> i.idText) |> String.concat "."
                    // Nested modules sit inside the parent scope; from the Fable
                    // emitter's perspective the parent kind is unchanged.
                    walkDecls ns (modules @ [moduleName]) isParentNamespace nestedDecls
                | _ -> ()

        match parseTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for SynModuleOrNamespace(longId = nsId; kind = kind; decls = decls) in modules do
                match kind with
                | SynModuleOrNamespaceKind.DeclaredNamespace ->
                    let ns = Some(nsId |> List.map (fun i -> i.idText) |> String.concat ".")
                    walkDecls ns [] true decls
                | SynModuleOrNamespaceKind.NamedModule ->
                    let moduleNames = nsId |> List.map (fun i -> i.idText)
                    walkDecls None moduleNames false decls
                | _ ->
                    walkDecls None [] false decls
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

    /// Compute the segment list of a TypeInfo's fully qualified name.
    /// e.g. `Namespace = Some "MyApp.Domain"; EnclosingModules = ["Forge"]; TypeName = "Project"`
    /// → `["MyApp"; "Domain"; "Forge"; "Project"]`.
    let private typeInfoFqnSegments (ti: TypeInfo) : string list =
        let nsSegments =
            match ti.Namespace with
            | Some ns when not (System.String.IsNullOrWhiteSpace ns) ->
                ns.Split('.') |> Array.toList
            | _ -> []
        nsSegments @ ti.EnclosingModules @ [ ti.TypeName ]

    let private buildFqn (ti: TypeInfo) =
        typeInfoFqnSegments ti |> String.concat "."

    /// Build a map from any FQN suffix (joined by `.`) to the list of TypeInfos
    /// whose FQN ends with that suffix. This lets a partially-qualified reference
    /// like `Forge.Project` resolve unambiguously even when another module defines
    /// a same-simple-named type.
    let private buildSuffixLookup (allTypeInfos: TypeInfo list) : Map<string, TypeInfo list> =
        let mutable acc : Map<string, TypeInfo list> = Map.empty
        for ti in allTypeInfos do
            let segs = typeInfoFqnSegments ti
            let len = List.length segs
            for n in 1 .. len do
                let suffix = segs |> List.skip (len - n) |> String.concat "."
                let existing = Map.tryFind suffix acc |> Option.defaultValue []
                acc <- Map.add suffix (ti :: existing) acc
        acc

    /// Resolve a (possibly partially-qualified) type reference to a single TypeInfo.
    /// Prefers a unique suffix match: `Forge.Project` will pick the TypeInfo whose
    /// FQN segments end with `["Forge"; "Project"]` even when another module defines
    /// a type named `Project`. Falls back to short-name lookup if no suffix matches
    /// or the suffix is ambiguous.
    let private resolveToTypeInfo
            (shortLookup: Map<string, TypeInfo>)
            (suffixLookup: Map<string, TypeInfo list>)
            (name: string) : TypeInfo option =
        match Map.tryFind name suffixLookup with
        | Some [ ti ] -> Some ti
        | Some (_ :: _) ->
            let dotIx = name.LastIndexOf '.'
            let shortName = if dotIx < 0 then name else name.Substring(dotIx + 1)
            Map.tryFind shortName shortLookup
        | _ ->
            match Map.tryFind name shortLookup with
            | Some ti -> Some ti
            | None ->
                let dotIx = name.LastIndexOf '.'
                if dotIx < 0 then None
                else Map.tryFind (name.Substring(dotIx + 1)) shortLookup

    /// Resolve a (possibly partially-qualified) type name to its fully qualified name.
    let private resolveTypeName (resolve: string -> TypeInfo option) (name: string) : string =
        match resolve name with
        | Some ti -> buildFqn ti
        | None -> name

    /// Discover all types transitively referenced from [<RpcApi>] interfaces.
    /// Returns both discovered types (for codec generation) and interface metadata (for RPC dispatch modules).
    let discover (allTypeInfos: TypeInfo list) (sourceFiles: (string * string) list) : RpcDiscoveryResult =
        // Build lookups early so we can resolve type names in method signatures.
        // The short-name lookup is used for transitive closure (which walks already-
        // resolved TypeInfo fields by short name); the suffix lookup is used to
        // disambiguate partially-qualified user references like `Forge.Project`.
        let lookup = buildLookup allTypeInfos
        let suffixLookup = buildSuffixLookup allTypeInfos
        let resolveTI = resolveToTypeInfo lookup suffixLookup
        let resolve = resolveTypeName resolveTI

        // Step 0: Parse each .fs file once and collect type abbreviations globally
        // so they can be expanded inside RpcApi signatures (e.g., `type PageSize = int`).
        let parsedFiles =
            sourceFiles
            |> List.choose (fun (filePath, sourceText) ->
                if filePath.EndsWith(".fs") then
                    parseFile filePath sourceText
                    |> Option.map (fun ast -> filePath, ast)
                else None)

        let aliases =
            parsedFiles
            |> List.fold (fun acc (_, ast) ->
                let fileAliases = collectAliasesFromAst ast
                Map.fold (fun m k v -> Map.add k v m) acc fileAliases) Map.empty

        // Step 1: Find all [<RpcApi>] interfaces and collect type names + method info
        let allCollected =
            parsedFiles
            |> List.map (fun (filePath, ast) ->
                try findRpcApis resolve aliases resolveTI filePath ast
                with _ ->
                    { TypeNames = ResizeArray<string>()
                      Interfaces = ResizeArray<RpcInterfaceInfo>()
                      TupleTypes = ResizeArray<TypeInfo>() })

        let rootTypeNames =
            allCollected
            |> List.collect (fun c -> Seq.toList c.TypeNames)
            |> List.distinct

        let interfaces =
            allCollected
            |> List.collect (fun c -> Seq.toList c.Interfaces)

        // Synthetic SerdeTypeInfo for tuple types found in method signatures.
        // De-duplicate using PascalName so identical tuple shapes are emitted once.
        let methodTupleSerdeTypes =
            allCollected
            |> List.collect (fun c -> Seq.toList c.TupleTypes)
            |> List.distinctBy typeInfoToPascalName
            |> List.map (fun ti ->
                { Raw = ti
                  Capability = SerdeCapability.Both
                  Attributes = SerdeAttributes.empty
                  ConverterType = None
                  CodecType = None
                  Fields = None
                  UnionCases = None
                  EnumCases = None
                  GenericContext = None } : SerdeTypeInfo)

        if rootTypeNames.IsEmpty then
            { DiscoveredTypes = methodTupleSerdeTypes; Interfaces = interfaces }
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

            { DiscoveredTypes = discoveredTypes @ methodTupleSerdeTypes; Interfaces = interfaces }
