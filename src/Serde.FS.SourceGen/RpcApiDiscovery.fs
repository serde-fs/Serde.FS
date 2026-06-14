namespace Serde.FS.SourceGen

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS

/// Discovers types transitively referenced from [<RpcApi>] interfaces.
/// Uses FSharpChecker directly because SourceDjinn does not parse interfaces.
/// Discovers `[<RpcApi>]` interfaces and their transitively-referenced types
/// from F# source files. Used by both the server-side codec generator
/// (`Serde.FS.Json.GeneratorHost`) and the Fable client generator
/// (`Serde.FS.Fable.GeneratorHost`).
module RpcApiDiscovery =

    let private checker = FSharpChecker.Create()

    let private identToString (idents: LongIdent) =
        idents |> List.map (fun i -> i.idText) |> String.concat "."

    let private rpcApiAttrNames = set [ "RpcApi"; "RpcApiAttribute" ]

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

    // ── Structural TypeInfo extraction ─────────────────────────────────────
    // Sibling of synTypeToString. Walks SynType and produces a TypeInfo that
    // emitters (like FableClientEmitter) can drive directly. This is the
    // single source of truth for type structure — no string parsing needed
    // downstream.

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

    /// Build a synthetic TypeInfo for a built-in / structural kind (Option,
    /// List, Tuple, Result, ...). These don't exist as user-declared types.
    let private mkSyntheticTypeInfo (typeName: string) (kind: TypeKind) : TypeInfo =
        { Namespace = None
          EnclosingModules = []
          TypeName = typeName
          Kind = kind
          Attributes = []
          GenericParameters = []
          GenericArguments = [] }

    /// Build a TypeInfo for a constructed generic. Used for Result<T,E> and
    /// any user-defined generic seen with type arguments.
    let private mkConstructedGeneric
        (baseTi: TypeInfo option)
        (typeName: string)
        (args: TypeInfo list)
        : TypeInfo
        =
        match baseTi with
        | Some baseTi ->
            { baseTi with
                Kind = TypeKind.ConstructedGenericType
                GenericArguments = args }
        | None ->
            { Namespace = None
              EnclosingModules = []
              TypeName = typeName
              Kind = TypeKind.ConstructedGenericType
              Attributes = []
              GenericParameters = []
              GenericArguments = args }

    /// Convert a SynType to a TypeInfo where possible. Walks the type tree
    /// structurally so the Fable emitter can drive codec naming + emission
    /// from a single source of truth (no string parsing).
    /// Returns None when a user type can't be resolved against `resolveTI`.
    let rec private synTypeToTypeInfo (resolveTI: string -> TypeInfo option) (synType: SynType) : TypeInfo option =
        match synType with
        | SynType.LongIdent(SynLongIdent(id = idents)) ->
            let name = identToString idents
            let shortName = idents |> List.last |> fun i -> i.idText
            match primitiveKindOf shortName with
            | Some pk -> Some (mkPrimitiveTypeInfo shortName pk)
            | None ->
                // Try qualified name first (e.g. "System.Guid"), then short.
                match resolveTI name with
                | Some _ as r -> r
                | None -> resolveTI shortName

        | SynType.App(typeName, _, typeArgs, _, _, _, _) ->
            let baseName =
                match typeName with
                | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
                | _ -> ""
            let argInfos = typeArgs |> List.map (synTypeToTypeInfo resolveTI)
            // Built-in collection / option / result wrappers emit structural Kinds.
            match baseName, argInfos with
            | ("option" | "Option"), [ Some inner ] ->
                Some (mkSyntheticTypeInfo "option" (Option inner))
            | ("list" | "List"), [ Some inner ] ->
                Some (mkSyntheticTypeInfo "list" (List inner))
            | ("array" | "Array"), [ Some inner ] ->
                Some (mkSyntheticTypeInfo "array" (Array inner))
            | ("seq" | "Seq"), [ Some inner ] ->
                // Seq has the same wire shape as list (JSON array), but we
                // keep TypeName="seq" so downstream emitters (e.g.
                // FableClientEmitter.fromTypeInfo) can distinguish and
                // generate seq-typed decode output, not list. Otherwise
                // record fields / member returns typed `seq<T>` reject the
                // emitted `list<T>` value with a type-mismatch.
                Some (mkSyntheticTypeInfo "seq" (List inner))
            | "Set", [ Some inner ] ->
                Some (mkSyntheticTypeInfo "Set" (Set inner))
            | "Map", [ Some k; Some v ] ->
                Some (mkSyntheticTypeInfo "Map" (Map (k, v)))
            | "Result", [ Some _; Some _ ] when argInfos |> List.forall Option.isSome ->
                let args = argInfos |> List.choose id
                Some (mkConstructedGeneric None "Result" args)
            | _ when argInfos |> List.forall Option.isSome ->
                // User-defined generic — look up the base definition and
                // reshape it as a constructed generic with the arg TypeInfos.
                let args = argInfos |> List.choose id
                Some (mkConstructedGeneric (resolveTI baseName) baseName args)
            | _ -> None

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
                Some (mkSyntheticTypeInfo "tuple" (Tuple fields))
            else None

        | SynType.Paren(inner, _) ->
            synTypeToTypeInfo resolveTI inner

        | SynType.Array(_, elementType, _) ->
            synTypeToTypeInfo resolveTI elementType
            |> Option.map (fun inner -> mkSyntheticTypeInfo "array" (Array inner))

        | SynType.Var _ ->
            // Open generic parameter — not resolvable at discovery time.
            None

        | SynType.Fun _ ->
            // Function-typed members aren't supported as RPC payloads.
            None

        | _ -> None

    /// Unwrap Async<T> or Task<T> and return the inner TypeInfo.
    let rec private unwrapAsyncTypeInfo (resolveTI: string -> TypeInfo option) (synType: SynType) : TypeInfo option =
        match synType with
        | SynType.App(typeName, _, [ innerType ], _, _, _, _) ->
            let baseName =
                match typeName with
                | SynType.LongIdent(SynLongIdent(id = idents)) -> identToString idents
                | _ -> ""
            if asyncWrapperNames.Contains baseName then
                synTypeToTypeInfo resolveTI innerType
            else
                synTypeToTypeInfo resolveTI synType
        | SynType.Paren(innerType, _) ->
            unwrapAsyncTypeInfo resolveTI innerType
        | _ ->
            synTypeToTypeInfo resolveTI synType

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
    let private extractMethodInfo
        (resolve: string -> string)
        (resolveTI: string -> TypeInfo option)
        (aliases: Map<string, SynType>)
        (valSig: SynValSig)
        : RpcMethodInfo option
        =
        let (SynValSig(ident = SynIdent(ident, _); synType = synType)) = valSig
        let expanded = expandAliases aliases synType
        let inputType, isTupled, inputParams, outputType = extractMethodTypes resolve expanded

        // Walk the same expanded SynType to produce structural TypeInfos.
        let inputTypeInfo, inputParamTypeInfos, outputTypeInfo =
            match expanded with
            | SynType.Fun(argType, returnType, _, _) ->
                let outputTI = unwrapAsyncTypeInfo resolveTI returnType
                match argType with
                | SynType.Tuple(_, segments, _) ->
                    // Multi-arg method: `abstract Foo: A * B -> C`
                    let perParam =
                        segments
                        |> List.choose (fun seg ->
                            match seg with
                            | SynTupleTypeSegment.Type t -> Some (synTypeToTypeInfo resolveTI t)
                            | _ -> None)
                    let composite = synTypeToTypeInfo resolveTI argType
                    composite, perParam, outputTI
                | _ ->
                    let inputTI = synTypeToTypeInfo resolveTI argType
                    inputTI, [], outputTI
            | _ ->
                // Property-style member with no arrow: input is unit, the entire
                // type is the output.
                let unitTI =
                    primitiveKindOf "unit"
                    |> Option.map (mkPrimitiveTypeInfo "unit")
                unitTI, [], synTypeToTypeInfo resolveTI expanded

        Some {
            MethodName = ident.idText
            InputType = inputType
            InputIsTupled = isTupled
            InputParams = inputParams
            OutputType = outputType
            InputTypeInfo = inputTypeInfo
            OutputTypeInfo = outputTypeInfo
            InputParamTypeInfos = inputParamTypeInfos
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


    /// Get the fully qualified name from a SynComponentInfo in the context of a namespace/modules.
    let private getTypeName (ns: string option) (modules: string list) (synComponentInfo: SynComponentInfo) : string * string =
        let (SynComponentInfo(longId = typeNameIdent)) = synComponentInfo
        let shortName = typeNameIdent |> List.map (fun i -> i.idText) |> String.concat "."
        let parts = [ yield! ns |> Option.toList; yield! modules; yield shortName ]
        let fullName = String.concat "." parts
        (fullName, shortName)

    // Note: primitiveKindOf, mkPrimitiveTypeInfo, mkSyntheticTypeInfo,
    // mkConstructedGeneric, and synTypeToTypeInfo are defined earlier in the
    // file (alongside synTypeToString) so extractMethodInfo can use them.

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
            match extractMethodInfo resolve resolveTI aliases slotSig with
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

                collected.Interfaces.Add({
                    FullName = fullName
                    ShortName = shortName
                    Methods = Seq.toList methods
                    Root = attrProps.Root
                    Version = attrProps.Version
                    UrlCaseValue = attrProps.UrlCaseValue
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

    /// Parent scope (FQN segments) of a resolved root TypeInfo — used to
    /// disambiguate field-type references that share a short name with another
    /// declaration. For a record at `CEI.BimHub.Domain.ConduitSchedule.Foo`,
    /// returns `["CEI"; "BimHub"; "Domain"; "ConduitSchedule"]`.
    let private parentScopeOfTypeInfo (ti: TypeInfo) : string list =
        let nsSegments =
            match ti.Namespace with
            | Some ns when not (System.String.IsNullOrWhiteSpace ns) ->
                ns.Split('.') |> Array.toList
            | _ -> []
        nsSegments @ ti.EnclosingModules

    /// Resolve a parser-captured field TypeInfo against a parent's lexical
    /// scope. Tries `parentScope ++ ti.EnclosingModules ++ [ti.TypeName]`
    /// (longest prefix first) using STRICT resolution (no short-name fallback —
    /// synthetic scope-walk candidates that don't fully match must be rejected,
    /// otherwise the forgiving fallback would arbitrarily pick a same-short-
    /// named homonym and produce a type mismatch in generated code). After all
    /// scope candidates fail, falls back to the bare partial-qualifier lookup
    /// (which IS forgiving, since the user-written name might itself be a
    /// partial qualifier that needs short-name salvage). Returns None when no
    /// candidate matches.
    let private resolveWithScope
            (resolveTIStrict: string -> TypeInfo option)
            (resolveTI: string -> TypeInfo option)
            (parentScope: string list)
            (ti: TypeInfo) : TypeInfo option =
        let userParts = ti.EnclosingModules @ [ ti.TypeName ]
        let scoped =
            let n = List.length parentScope
            seq {
                for take in n .. -1 .. 1 do
                    let prefix = parentScope |> List.take take
                    yield String.concat "." (prefix @ userParts)
            }
            |> Seq.tryPick resolveTIStrict
        match scoped with
        | Some _ -> scoped
        | None -> resolveTI (String.concat "." userParts)

    /// Recursively collect FQNs of all types transitively reachable from `ti`'s
    /// fields and union-case fields. `parentScope` of nested-field resolution
    /// is derived from each parent's FQN as we descend, so unqualified field
    /// references like `Conduit: Conduit` in module `ConduitSchedule` resolve
    /// to `ConduitSchedule.Conduit` (and the homonym in `FeederRelease` to
    /// `FeederRelease.Conduit`). Both candidates are discovered when both are
    /// referenced — the previous string-keyed walker collapsed them onto one.
    /// `visited` is keyed by FQN so two types with the same short name stay
    /// distinct.
    let rec private collectTransitive
            (resolveTIStrict: string -> TypeInfo option)
            (resolveTI: string -> TypeInfo option)
            (visited: Set<string>)
            (ti: TypeInfo) : Set<string> =
        let identifier = buildFqn ti
        if visited.Contains identifier then visited
        else
            let visited = visited.Add identifier
            let parentScope = parentScopeOfTypeInfo ti
            let fields =
                match ti.Kind with
                | Record fs | AnonymousRecord fs -> fs
                | Union cs -> cs |> List.collect (fun c -> c.Fields)
                | _ -> []
            fields
            |> List.fold
                (fun acc f -> collectFieldDescendants resolveTIStrict resolveTI acc parentScope f.Type)
                visited

    /// Walk a single field TypeInfo, recursing into collection wrappers and
    /// constructed-generic arguments. At each Record/Union/Enum leaf, resolve
    /// against `parentScope` (the enclosing type's FQN segments) so ambiguous
    /// short-name references pick the correct candidate.
    and private collectFieldDescendants
            (resolveTIStrict: string -> TypeInfo option)
            (resolveTI: string -> TypeInfo option)
            (visited: Set<string>)
            (parentScope: string list)
            (fieldTi: TypeInfo) : Set<string> =
        match fieldTi.Kind with
        | Primitive _ | GenericParameter _ | GenericTypeDefinition _ -> visited
        | Option inner | List inner | Array inner | Set inner ->
            collectFieldDescendants resolveTIStrict resolveTI visited parentScope inner
        | Map (k, v) ->
            let visited = collectFieldDescendants resolveTIStrict resolveTI visited parentScope k
            collectFieldDescendants resolveTIStrict resolveTI visited parentScope v
        | Tuple fs ->
            fs
            |> List.fold
                (fun acc f -> collectFieldDescendants resolveTIStrict resolveTI acc parentScope f.Type)
                visited
        | ConstructedGenericType ->
            let argVisited =
                fieldTi.GenericArguments
                |> List.fold
                    (fun acc arg -> collectFieldDescendants resolveTIStrict resolveTI acc parentScope arg)
                    visited
            if skipTypeNames.Contains fieldTi.TypeName then argVisited
            else
                match resolveWithScope resolveTIStrict resolveTI parentScope fieldTi with
                | Some resolved -> collectTransitive resolveTIStrict resolveTI argVisited resolved
                | None -> argVisited
        | Record _ | Union _ | Enum _ | AnonymousRecord _ ->
            if skipTypeNames.Contains fieldTi.TypeName then visited
            else
                match resolveWithScope resolveTIStrict resolveTI parentScope fieldTi with
                | Some resolved -> collectTransitive resolveTIStrict resolveTI visited resolved
                | None -> visited

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

    /// Strict suffix-only resolver: returns Some only when `name` matches the
    /// FQN suffix of exactly one declared type. No short-name fallback, no
    /// arbitrary-pick on ambiguity. Used when walking synthetic candidates
    /// (e.g. parentScope-prepended names) — a synthetic candidate that doesn't
    /// match exactly must be rejected; otherwise the forgiving fallback would
    /// salvage it as a same-short-name homonym and emit the wrong codec.
    let private resolveToTypeInfoStrict
            (suffixLookup: Map<string, TypeInfo list>)
            (name: string) : TypeInfo option =
        match Map.tryFind name suffixLookup with
        | Some [ ti ] -> Some ti
        | _ -> None

    /// Forgiving resolver: prefers a unique suffix match, then falls back to
    /// short-name lookup. Use this for user-written names (which may be
    /// partial qualifiers that need short-name salvage), NOT for synthetic
    /// scope-walk candidates (use resolveToTypeInfoStrict for those).
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
        let resolveTIStrict = resolveToTypeInfoStrict suffixLookup
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

        // Resolve a field's parser-captured TypeInfo to its canonical FQN form.
        //   • Alias (e.g. `type SheetNumber = Guid`) — expand to target TypeInfo
        //     by running synTypeToTypeInfo on the alias's SynType. Returns the
        //     primitive/structural target so the codec emitter emits e.g.
        //     `IJsonCodec<Guid>` instead of `IJsonCodec<SheetNumber>`.
        //   • Unqualified short name with lexical-scope disambiguation: if both
        //     `ConduitSchedule.Conduit` and `FeederRelease.Conduit` exist and a
        //     record IN `ConduitSchedule` declares `field: Conduit`, prefer the
        //     same-module `ConduitSchedule.Conduit` (F#'s scoping rule). Tries
        //     `parentScope ++ ti.EnclosingModules ++ [ti.TypeName]` first, then
        //     walks up the parent's enclosing modules.
        //   • Partial qualifier (e.g. field `Hub: Forge.Hub`, parser captures
        //     TypeName="Forge.Hub" with empty namespace) — suffix-lookup to the
        //     unique declaration, then copy its canonical Namespace /
        //     EnclosingModules / TypeName onto the field's TypeInfo so
        //     typeInfoToFqFSharpType produces the full "CEI.BimHub.Forge.Hub".
        //   • Already-resolved or built-in types — returned unchanged.
        //
        // `parentScope` is the FQN segments of the type whose field we're
        // resolving (e.g. ["CEI"; "BimHub"; "Domain"; "ConduitSchedule"] for a
        // field of `CEI.BimHub.Domain.ConduitSchedule.SomeRecord`). Pass [] when
        // there's no containing type (e.g. root-level Serde<T> calls).
        //
        // Only fires on Record / Union / Enum / AnonymousRecord /
        // ConstructedGenericType kinds; collection wrappers (Option/List/etc.)
        // are walked by the caller (SerdeGeneratorEngine.FieldTypeResolver).
        let resolveFieldType (parentScope: string list) (ti: TypeInfo) : TypeInfo =
            match ti.Kind with
            | Record _ | Union _ | Enum _ | AnonymousRecord _ | ConstructedGenericType ->
                match Map.tryFind ti.TypeName aliases with
                | Some aliasTarget ->
                    match synTypeToTypeInfo resolveTI aliasTarget with
                    | Some targetTi -> targetTi
                    | None -> ti
                | None ->
                    let userParts = ti.EnclosingModules @ [ ti.TypeName ]
                    // Try lexical-scope candidates first: longest parent prefix
                    // wins so the same-module type beats outer-module homonyms.
                    // STRICT resolution — a synthetic candidate that doesn't
                    // match a unique declaration must be rejected (else the
                    // forgiving fallback would salvage it as a homonym and
                    // emit the wrong codec; see issue with `Forge.Project`
                    // vs `Project.Project`).
                    let scopedCandidate =
                        let n = List.length parentScope
                        seq {
                            for take in n .. -1 .. 1 do
                                let prefix = parentScope |> List.take take
                                yield String.concat "." (prefix @ userParts)
                        }
                        |> Seq.tryPick resolveTIStrict
                    let partialCandidate () =
                        resolveTI (String.concat "." userParts)
                    match scopedCandidate |> Option.orElseWith partialCandidate with
                    | Some resolved ->
                        { ti with
                            Namespace = resolved.Namespace
                            EnclosingModules = resolved.EnclosingModules
                            TypeName = resolved.TypeName }
                    | None -> ti
            | _ -> ti

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

        let aliasNames = aliases |> Map.toSeq |> Seq.map fst |> Set.ofSeq

        if rootTypeNames.IsEmpty then
            { DiscoveredTypes = methodTupleSerdeTypes
              Interfaces = interfaces
              AliasNames = aliasNames
              ResolveFieldType = resolveFieldType }
        else
            // Step 2: Compute transitive closure. Resolve each root method-arg
            // name to a TypeInfo, then descend through its fields with
            // parent-scope-aware resolution at each Record/Union/Enum leaf.
            // `visited` accumulates canonical FQN strings (so two different
            // types sharing a short name stay distinct).
            let rootTypeInfos =
                rootTypeNames
                |> List.filter (fun n -> not (skipTypeNames.Contains n))
                |> List.choose resolveTI
                |> List.distinctBy buildFqn

            let allDiscoveredNames =
                rootTypeInfos
                |> List.fold (fun acc ti -> collectTransitive resolveTIStrict resolveTI acc ti) Set.empty

            // Step 3: Build SerdeTypeInfo for each discovered type. The FQN
            // strings in `visited` map back to the correct TypeInfo via
            // resolveTI (suffix lookup is unique when the FQN is given in full).
            let discoveredTypes =
                allDiscoveredNames
                |> Set.toList
                |> List.choose resolveTI
                |> List.filter (fun ti ->
                    match ti.Kind with
                    | Record _ | Union _ | Enum _ | AnonymousRecord _ -> true
                    | _ -> false)
                |> List.map SerdeMetadataBuilder.buildSerdeTypeInfo

            { DiscoveredTypes = discoveredTypes @ methodTupleSerdeTypes
              Interfaces = interfaces
              AliasNames = aliasNames
              ResolveFieldType = resolveFieldType }
