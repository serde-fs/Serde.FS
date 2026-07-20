namespace Serde.FS.SourceGen

open Serde.FS
open FSharp.SourceDjinn
open FSharp.SourceDjinn.TypeModel.Types

module private OptionDiscovery =

    /// Recursively collects all distinct option TypeInfos from a TypeInfo.
    let rec private collectOptionTypes (ti: TypeInfo) (acc: Map<string, TypeInfo>) : Map<string, TypeInfo> =
        match ti.Kind with
        | Option inner ->
            // Collect inner first (dependency order)
            let acc = collectOptionTypes inner acc
            let key = typeInfoToPascalName ti
            if acc |> Map.containsKey key then acc
            else acc |> Map.add key ti
        | Tuple elements ->
            elements |> List.fold (fun a f -> collectOptionTypes f.Type a) acc
        | List inner | Array inner | Set inner ->
            collectOptionTypes inner acc
        | Map (k, v) ->
            let acc = collectOptionTypes k acc
            collectOptionTypes v acc
        | _ -> acc

    /// Scans all record types' fields and returns distinct option TypeInfos in dependency order.
    let discoverOptionTypes (types: SerdeTypeInfo seq) : TypeInfo list =
        let mutable acc = Map.empty
        for t in types do
            match t.Fields with
            | Some fields ->
                for f in fields do
                    acc <- collectOptionTypes f.Type acc
            | None -> ()
        acc |> Map.toList |> List.map snd

    /// Creates a synthetic SerdeTypeInfo for an option TypeInfo.
    let mkOptionSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        {
            Raw = ti
            Capability = Both
            Attributes = SerdeAttributes.empty
            ConverterType = None
            CodecType = None
            Fields = None
            UnionCases = None
            EnumCases = None
            GenericContext = None
        }

module private TupleDiscovery =

    /// Recursively collects all distinct tuple TypeInfos from a TypeInfo.
    let rec private collectTupleTypes (ti: TypeInfo) (acc: Map<string, TypeInfo>) : Map<string, TypeInfo> =
        match ti.Kind with
        | Tuple elements ->
            // Collect inner tuples first (dependency order)
            let acc = elements |> List.fold (fun a f -> collectTupleTypes f.Type a) acc
            let key = typeInfoToPascalName ti
            if acc |> Map.containsKey key then acc
            else acc |> Map.add key ti
        | Option inner | List inner | Array inner | Set inner ->
            collectTupleTypes inner acc
        | Map (k, v) ->
            let acc = collectTupleTypes k acc
            collectTupleTypes v acc
        | _ -> acc

    /// Scans all types' fields and returns distinct tuple TypeInfos in dependency order.
    let discoverTupleTypes (types: SerdeTypeInfo seq) : TypeInfo list =
        let mutable acc = Map.empty
        for t in types do
            match t.Fields with
            | Some fields ->
                for f in fields do
                    acc <- collectTupleTypes f.Type acc
            | None -> ()
        acc |> Map.toList |> List.map snd

    /// Creates a synthetic SerdeTypeInfo for a tuple TypeInfo.
    let mkTupleSerdeTypeInfo (ti: TypeInfo) : SerdeTypeInfo =
        {
            Raw = ti
            Capability = Both
            Attributes = SerdeAttributes.empty
            ConverterType = None
            CodecType = None
            Fields = None
            UnionCases = None
            EnumCases = None
            GenericContext = None
        }

module private GenericDiscovery =

    /// Key for looking up generic definitions: (namespace, name, arity)
    type SerdeDefinitionKey = string option * string * int

    let definitionKey (ti: TypeInfo) : SerdeDefinitionKey =
        (ti.Namespace, ti.TypeName, ti.GenericParameters.Length)

    /// Build a map of Serde generic definitions from all parsed Serde types.
    let buildDefinitionMap (types: SerdeTypeInfo seq) : Map<SerdeDefinitionKey, SerdeTypeInfo> =
        types
        |> Seq.filter (fun t -> t.Raw.IsGenericDefinition)
        |> Seq.map (fun t -> definitionKey t.Raw, t)
        |> Map.ofSeq

    /// Try to find the Serde definition for a constructed generic TypeInfo.
    let tryFindDefinition (definitions: Map<SerdeDefinitionKey, SerdeTypeInfo>) (constructed: TypeInfo) : SerdeTypeInfo option =
        if not constructed.IsConstructedGeneric then None
        else
            let key = (constructed.Namespace, constructed.TypeName, constructed.GenericArguments.Length)
            Map.tryFind key definitions

    /// Recursively collects all constructed generic TypeInfos from a TypeInfo.
    let rec private collectConstructedGenerics (ti: TypeInfo) (acc: Map<string, TypeInfo>) : Map<string, TypeInfo> =
        match ti.Kind with
        | ConstructedGenericType ->
            // Recurse into generic arguments first (dependency order: inner generics first)
            let acc = ti.GenericArguments |> List.fold (fun a arg -> collectConstructedGenerics arg a) acc
            let key = typeInfoToPascalName ti
            if acc |> Map.containsKey key then acc
            else acc |> Map.add key ti
        | Option inner | List inner | Array inner | Set inner ->
            collectConstructedGenerics inner acc
        | Map (k, v) ->
            let acc = collectConstructedGenerics k acc
            collectConstructedGenerics v acc
        | Tuple elements ->
            elements |> List.fold (fun a f -> collectConstructedGenerics f.Type a) acc
        | _ -> acc

    /// Scan all fields and union case fields of all Serde types, returning distinct
    /// constructed generic TypeInfos in dependency order.
    let discoverConstructedGenerics (types: SerdeTypeInfo seq) : TypeInfo list =
        let mutable acc = Map.empty
        for t in types do
            match t.Fields with
            | Some fields ->
                for f in fields do
                    acc <- collectConstructedGenerics f.Type acc
            | None -> ()
            match t.UnionCases with
            | Some cases ->
                for c in cases do
                    for f in c.Fields do
                        acc <- collectConstructedGenerics f.Type acc
            | None -> ()
        acc |> Map.toList |> List.map snd

    /// Scan a list of TypeInfos directly for constructed generics (used for root-level type args).
    let discoverFromTypeInfos (typeInfos: TypeInfo list) : TypeInfo list =
        let mutable acc = Map.empty
        for ti in typeInfos do
            acc <- collectConstructedGenerics ti acc
        acc |> Map.toList |> List.map snd

    /// Build a SerdeTypeInfo for a constructed generic by instantiating its definition.
    let buildConstructedSerdeTypeInfo (defInfo: SerdeTypeInfo) (constructed: TypeInfo) : SerdeTypeInfo =
        let instantiated = TypeInfo.instantiate defInfo.Raw constructed.GenericArguments
        // Preserve namespace/modules from the constructed reference
        let instantiated = { instantiated with Namespace = constructed.Namespace; EnclosingModules = constructed.EnclosingModules }
        let baseInfo = SerdeMetadataBuilder.buildSerdeTypeInfo instantiated
        { baseInfo with
            GenericContext = Some {
                DefinitionType = defInfo.Raw
                GenericParameters = defInfo.Raw.GenericParameters
                GenericArguments = constructed.GenericArguments
            } }

/// Walks SerdeTypeInfo / TypeInfo trees and applies a single-type resolver
/// (typically `RpcDiscoveryResult.ResolveFieldType`) at each leaf, normalising
/// parser-captured field TypeInfos to their canonical (Namespace + EM + TN)
/// form. Public so design-time tools outside SerdeGeneratorEngine — most
/// notably `Serde.FS.Fable.GeneratorHost`, which builds Fable clients
/// without going through the full codec pipeline — can apply the same
/// normalization the server-side generator does. Without it, codec module
/// names emitted by the Fable client emitter would diverge from those the
/// server emits (e.g. bare `ConduitCodec` vs disambiguated
/// `ConduitSchedule_ConduitCodec`), and the generated client wouldn't compile.
module FieldTypeResolver =

    /// FQN segments of the type whose fields we're resolving. Used as the
    /// `parentScope` to disambiguate unqualified short names (e.g. a field
    /// `Conduit: Conduit` resolves to the same-module Conduit). Empty when
    /// there's no containing type (root-level Serde<T> calls).
    let private parentScopeOf (ti: TypeInfo) : string list =
        let nsSegments =
            match ti.Namespace with
            | Some ns when not (System.String.IsNullOrWhiteSpace ns) ->
                ns.Split('.') |> Array.toList
            | _ -> []
        nsSegments @ ti.EnclosingModules

    /// Recursively normalise a field's TypeInfo. The single-type resolver
    /// `resolveSingle` is provided by RpcApiDiscovery — it knows about partial
    /// qualifiers (via suffix lookup), F# type abbreviations, and lexical
    /// scoping (via the parentScope passed in). This walker descends through
    /// collection wrappers (Option/List/Map/Tuple/...) and calls
    /// `resolveSingle` at each Record/Union/Enum/ConstructedGeneric leaf so
    /// they get canonical FQN identity.
    let rec resolveTypeInfo (resolveSingle: string list -> TypeInfo -> TypeInfo) (parentScope: string list) (ti: TypeInfo) : TypeInfo =
        match ti.Kind with
        | Primitive _ | GenericParameter _ | GenericTypeDefinition _ -> ti
        | ConstructedGenericType ->
            let resolved = resolveSingle parentScope ti
            { resolved with
                GenericArguments = resolved.GenericArguments |> List.map (resolveTypeInfo resolveSingle parentScope) }
        | Record _ | AnonymousRecord _ | Union _ | Enum _ ->
            resolveSingle parentScope ti
        | Option inner -> { ti with Kind = Option (resolveTypeInfo resolveSingle parentScope inner) }
        | List inner -> { ti with Kind = List (resolveTypeInfo resolveSingle parentScope inner) }
        | Array inner -> { ti with Kind = Array (resolveTypeInfo resolveSingle parentScope inner) }
        | Set inner -> { ti with Kind = Set (resolveTypeInfo resolveSingle parentScope inner) }
        | Map (k, v) ->
            { ti with Kind = Map (resolveTypeInfo resolveSingle parentScope k, resolveTypeInfo resolveSingle parentScope v) }
        | Tuple fields ->
            { ti with
                Kind = Tuple (fields |> List.map (fun f -> { f with Type = resolveTypeInfo resolveSingle parentScope f.Type })) }

    let resolveSerdeTypeInfo (resolveSingle: string list -> TypeInfo -> TypeInfo) (sti: SerdeTypeInfo) : SerdeTypeInfo =
        let parentScope = parentScopeOf sti.Raw
        let resolvedFields =
            match sti.Fields with
            | Some fields ->
                Some (fields |> List.map (fun f -> { f with Type = resolveTypeInfo resolveSingle parentScope f.Type }))
            | None -> None
        let resolvedUnionCases =
            match sti.UnionCases with
            | Some cases ->
                Some (cases |> List.map (fun c ->
                    { c with Fields = c.Fields |> List.map (fun f -> { f with Type = resolveTypeInfo resolveSingle parentScope f.Type }) }))
            | None -> None
        { sti with Fields = resolvedFields; UnionCases = resolvedUnionCases }

module internal NestedTypeValidator =

    let private fullyQualifiedName (ti: TypeInfo) : string =
        let parts =
            [ yield! ti.Namespace |> Option.toList
              yield! ti.EnclosingModules
              yield ti.TypeName ]
        let baseName = String.concat "." parts
        if ti.IsConstructedGeneric then
            let argNames = ti.GenericArguments |> List.map typeInfoToFqFSharpType
            sprintf "%s<%s>" baseName (String.concat ", " argNames)
        else baseName

    let rec private validateTypeInfo (serdeNames: Set<string>) (ti: TypeInfo) : string list =
        match ti.Kind with
        | Primitive _ | GenericParameter _ | GenericTypeDefinition _ -> []
        | ConstructedGenericType ->
            // Constructed generics are valid if they were discovered and added to serdeNames
            let fqn = fullyQualifiedName ti
            if serdeNames.Contains fqn then []
            else
                // Validate each generic argument individually
                ti.GenericArguments |> List.collect (validateTypeInfo serdeNames)
        | Option inner | List inner | Array inner | Set inner ->
            validateTypeInfo serdeNames inner
        | Map (k, v) ->
            validateTypeInfo serdeNames k @ validateTypeInfo serdeNames v
        | Tuple elements ->
            elements |> List.collect (fun f -> validateTypeInfo serdeNames f.Type)
        | AnonymousRecord fields ->
            fields |> List.collect (fun f -> validateTypeInfo serdeNames f.Type)
        | Record _ | Union _ | Enum _ ->
            let fqn = fullyQualifiedName ti
            if serdeNames.Contains fqn then []
            else
                [ sprintf "Serde error: Type '%s' is used in serialization but does not have Serde metadata. Add [<Serde>] to the type definition." fqn ]

    let validate (serdeNames: Set<string>) (types: SerdeTypeInfo list) : string list =
        let errors = ResizeArray<string>()
        for t in types do
            match t.Fields with
            | Some fields -> for f in fields do errors.AddRange(validateTypeInfo serdeNames f.Type)
            | None -> ()
            match t.UnionCases with
            | Some cases -> for c in cases do for f in c.Fields do errors.AddRange(validateTypeInfo serdeNames f.Type)
            | None -> ()
        errors |> Seq.distinct |> Seq.toList

module SerdeGeneratorEngine =

    type GeneratedSource = {
        HintName: string
        Code: string
        /// When Some, the source is written to this absolute file path instead of
        /// the generator's default output directory. Used for cross-project emissions
        /// (e.g., the Fable client is written into the Shared project, not the Server's obj).
        AbsolutePath: string option
    }

    type GeneratorResult = {
        Sources: GeneratedSource list
        Errors: string list
        Warnings: string list
    }

    let generate (sourceFiles: (string * string) list) (emitter: ISerdeCodeEmitter) : GeneratorResult =
        let errors = ResizeArray<string>()
        let warnings = ResizeArray<string>()
        let mutable success = true
        let parsedTypes = System.Collections.Generic.List<SerdeTypeInfo>()
        let allTypeInfos = System.Collections.Generic.List<TypeInfo>()
        let allTypes = System.Collections.Generic.List<SerdeTypeInfo>()
        let generatedSources = ResizeArray<GeneratedSource>()

        // Phase 1: Parse all source files
        let rootTypeArgs = System.Collections.Generic.List<TypeInfo>()
        for (filePath, sourceText) in sourceFiles do
            if filePath.EndsWith(".fs") then
                try
                    let types = SerdeAstParser.parseSource filePath sourceText
                    parsedTypes.AddRange(types)

                    let allTypesInFile = SerdeAstParser.parseSourceAllTypes filePath sourceText
                    allTypeInfos.AddRange(allTypesInFile)

                    let typeArgs = SerdeAstParser.parseSourceRootTypeArgs filePath sourceText
                    rootTypeArgs.AddRange(typeArgs)
                with ex ->
                    warnings.Add(sprintf "Serde: Failed to process %s: %s" filePath ex.Message)

        // Phase 1.5: Discover types from [<RpcApi>] interfaces
        let existingTypeNames =
            parsedTypes
            |> Seq.map (fun t -> t.Raw.TypeName)
            |> Set.ofSeq
        let rpcDiscoveryResult = RpcApiDiscovery.discover (Seq.toList allTypeInfos) sourceFiles
        let rpcApiTypes =
            rpcDiscoveryResult.DiscoveredTypes
            |> List.filter (fun t -> not (existingTypeNames.Contains t.Raw.TypeName))
        parsedTypes.AddRange(rpcApiTypes)

        // Phase 2: Resolve field type references across all parsed types
        let lookup =
            allTypeInfos
            |> Seq.map (fun t -> t.TypeName, t)
            |> Map.ofSeq
        let resolveTypeName (sti: SerdeTypeInfo) (name: string) : string =
            match Map.tryFind name lookup with
            | Some ti ->
                [ yield! ti.Namespace |> Option.toList
                  yield! ti.EnclosingModules
                  yield ti.TypeName ]
                |> String.concat "."
            | None ->
                let prefix =
                    [ yield! sti.Raw.Namespace |> Option.toList
                      yield! sti.Raw.EnclosingModules ]
                if prefix.IsEmpty then name
                else String.concat "." (prefix @ [name])

        let resolveFieldCodecTypes (sti: SerdeTypeInfo) : SerdeTypeInfo =
            match sti.Fields with
            | Some fields ->
                let resolvedFields =
                    fields |> List.map (fun f ->
                        match f.CodecType with
                        | Some name -> { f with CodecType = Some (resolveTypeName sti name) }
                        | None -> f)
                { sti with Fields = Some resolvedFields }
            | None -> sti

        let resolvedTypes =
            parsedTypes
            |> Seq.map (FieldTypeResolver.resolveSerdeTypeInfo rpcDiscoveryResult.ResolveFieldType)
            |> Seq.map (fun sti ->
                let sti =
                    match sti.ConverterType with
                    | Some name -> { sti with ConverterType = Some (resolveTypeName sti name) }
                    | None -> sti
                let sti =
                    match sti.CodecType with
                    | Some name -> { sti with CodecType = Some (resolveTypeName sti name) }
                    | None -> sti
                resolveFieldCodecTypes sti)
            |> Seq.toList

        // Phase 2.1: Detect root-level constructed generics without explicit type args
        let projectDir = System.IO.Directory.GetCurrentDirectory()
        for (filePath, sourceText) in sourceFiles do
            if filePath.EndsWith(".fs") then
                try
                    let diagnostics = RootGenericDiagnostics.detect resolvedTypes lookup projectDir filePath sourceText
                    for d in diagnostics do
                        errors.Add(RootGenericDiagnostics.formatMessage d)
                        success <- false
                with ex ->
                    warnings.Add(sprintf "Serde: Failed root-generic diagnostic on %s: %s" filePath ex.Message)

        // Phase 2.3: Discover constructed generics from fields/union cases and root-level calls
        let genericDefinitions = GenericDiscovery.buildDefinitionMap resolvedTypes
        let fieldConstructedGenerics = GenericDiscovery.discoverConstructedGenerics resolvedTypes

        let rootConstructed =
            rootTypeArgs
            |> Seq.map (FieldTypeResolver.resolveTypeInfo rpcDiscoveryResult.ResolveFieldType [])
            |> Seq.toList
            |> GenericDiscovery.discoverFromTypeInfos

        let constructedGenerics =
            let mutable acc = fieldConstructedGenerics |> List.map (fun ti -> typeInfoToPascalName ti, ti) |> Map.ofList
            for ti in rootConstructed do
                let key = typeInfoToPascalName ti
                if not (acc |> Map.containsKey key) then
                    acc <- acc |> Map.add key ti
            acc |> Map.toList |> List.map snd

        let knownSerdeTypeNames =
            resolvedTypes
            |> Seq.map (fun t ->
                let parts =
                    [ yield! t.Raw.Namespace |> Option.toList
                      yield! t.Raw.EnclosingModules
                      yield t.Raw.TypeName ]
                String.concat "." parts)
            |> Set.ofSeq

        let constructedSerdeTypes = System.Collections.Generic.List<SerdeTypeInfo>()
        let mutable genericErrors = []

        // Built-in generic wrappers handled at runtime by codec factories
        // (Result, Map, Set, etc. via Serde.FS.Json.Codec). They're Serde-enabled
        // as long as their type arguments are — the user doesn't need a
        // [<Serde>] definition for the wrapper itself.
        let isBuiltInGeneric (name: string) =
            match name with
            | "Result" | "option" | "Option" | "list" | "List"
            | "array" | "Array" | "seq" | "Seq" | "Set" | "Map" -> true
            | _ -> false

        let rec isSerdeEnabled (ti: TypeInfo) =
            match ti.Kind with
            | Primitive _ | GenericParameter _ | GenericTypeDefinition _ -> true
            | Option _ | List _ | Array _ | Set _ | Map _ | Tuple _ -> true
            | ConstructedGenericType when isBuiltInGeneric ti.TypeName ->
                ti.GenericArguments |> List.forall isSerdeEnabled
            | ConstructedGenericType ->
                GenericDiscovery.tryFindDefinition genericDefinitions ti |> Option.isSome
                && ti.GenericArguments |> List.forall isSerdeEnabled
            | Record _ | Union _ | Enum _ | AnonymousRecord _ ->
                let fqn =
                    let parts =
                        [ yield! ti.Namespace |> Option.toList
                          yield! ti.EnclosingModules
                          yield ti.TypeName ]
                    String.concat "." parts
                knownSerdeTypeNames.Contains fqn

        // Helper: detect generic single-case single-field DU definitions (factory-based)
        let isGenericSingleCaseWrapperDef (defInfo: SerdeTypeInfo) =
            defInfo.Raw.IsGenericDefinition
            && match defInfo.UnionCases with
               | Some [case] when case.Fields.Length = 1 -> true
               | _ -> false

        // Built-in generic types handled by runtime codec factories (registered
        // by Serde.FS.Json itself, not the user). When a Domain record has a
        // field like `Result<X, string>` or `Map<int, string>`, we don't need a
        // user-defined definition for the wrapper — only the type arguments
        // need to be Serde-enabled. Skipping them here avoids "X is not marked
        // with [<Serde>]" errors for built-ins the user never declared.
        let builtInGenerics =
            Set.ofList [
                "Result"
                "option"; "Option"
                "list"; "List"
                "array"; "Array"
                "seq"; "Seq"
                "Set"; "Map"
            ]

        for constructed in constructedGenerics do
            if builtInGenerics.Contains constructed.TypeName then
                // Built-in generic: validate args only, codec emitted by the
                // backend's runtime factory.
                for arg in constructed.GenericArguments do
                    if not (isSerdeEnabled arg) then
                        let fqn = typeInfoToFqFSharpType constructed
                        let argName = typeInfoToFqFSharpType arg
                        genericErrors <-
                            (sprintf "Serde.FS error: The generic type '%s' cannot be used with Serde because its type argument '%s' is not Serde-enabled."
                                fqn argName) :: genericErrors
            else
                match GenericDiscovery.tryFindDefinition genericDefinitions constructed with
                | Some defInfo ->
                    // Skip concrete instantiations of generic single-case wrapper DUs (factory handles them)
                    if isGenericSingleCaseWrapperDef defInfo then () else
                    let mutable argValid = true
                    for arg in constructed.GenericArguments do
                        if not (isSerdeEnabled arg) then
                            argValid <- false
                            let fqn = typeInfoToFqFSharpType constructed
                            let argName = typeInfoToFqFSharpType arg
                            genericErrors <- (sprintf "Serde.FS error: The generic type '%s' cannot be used with Serde because its type argument '%s' is not Serde-enabled." fqn argName) :: genericErrors
                    if argValid then
                        let serdeInfo = GenericDiscovery.buildConstructedSerdeTypeInfo defInfo constructed
                        constructedSerdeTypes.Add(serdeInfo)
                | None ->
                    let fqn = typeInfoToFqFSharpType constructed
                    genericErrors <- (sprintf "Serde error: Type '%s' is used in serialization, but '%s' is not marked with [<Serde>]." fqn constructed.TypeName) :: genericErrors

        for msg in genericErrors do
            errors.Add(msg)
        if not (List.isEmpty genericErrors) then
            success <- false

        let resolvedTypes = resolvedTypes @ (constructedSerdeTypes |> Seq.toList)

        // Phase 2.5: Validate nested user-defined types have Serde metadata.
        // The set includes EVERY suffix of each resolved type's FQN
        // (e.g. "CEI.BimHub.Forge.Hub" → "Hub", "Forge.Hub", "BimHub.Forge.Hub",
        // "CEI.BimHub.Forge.Hub"). This way a field referenced via partial
        // qualifier — `Hub : Forge.Hub`, whose TypeInfo parser captures as
        // TypeName="Forge.Hub" with empty namespace info — still matches against
        // the actually-discovered "CEI.BimHub.Forge.Hub". The validator only
        // needs to catch "user forgot to annotate", so over-acceptance from
        // multiple types sharing a suffix is acceptable.
        let fqnSuffixes (baseName: string) : string seq =
            let segments = baseName.Split('.')
            seq {
                for i in 0 .. segments.Length - 1 ->
                    System.String.Join(".", segments, i, segments.Length - i)
            }

        let serdeTypeNames =
            let resolvedNames =
                resolvedTypes
                |> Seq.collect (fun t ->
                    let parts =
                        [ yield! t.Raw.Namespace |> Option.toList
                          yield! t.Raw.EnclosingModules
                          yield t.Raw.TypeName ]
                    let baseName = String.concat "." parts
                    let fullName =
                        match t.GenericContext with
                        | Some ctx ->
                            let argNames = ctx.GenericArguments |> List.map typeInfoToFqFSharpType
                            sprintf "%s<%s>" baseName (String.concat ", " argNames)
                        | None -> baseName
                    // Include the full constructed form (when generic) AND every
                    // suffix of the base name so partial-qualifier field references
                    // (e.g. "Forge.Hub") match the resolved type's full FQN.
                    seq {
                        yield fullName
                        yield! fqnSuffixes baseName
                    })
            // F# type abbreviations (`type SheetNumber = Guid`) erase at compile
            // time and never appear as their own TypeInfo, so the validator
            // would otherwise complain about a field declared `Id: SheetNumber`.
            // Treat aliases as known so they pass — the underlying target
            // (Guid/string/etc.) already has a primitive codec at runtime.
            Seq.append resolvedNames rpcDiscoveryResult.AliasNames
            |> Set.ofSeq
        let violations = NestedTypeValidator.validate serdeTypeNames resolvedTypes
        for msg in violations do
            errors.Add(msg)
        if not (List.isEmpty violations) then
            success <- false

        if success then
            // Determine whether per-type files should be emitted
            let emitPerTypeFiles =
                match emitter with
                | :? ISerdeResolverEmitter as re -> re.EmitPerTypeFiles
                | _ -> true

            // Phase 3: Emit all types (skip generic definitions, except single-case wrapper DU factories)
            for typeInfo in resolvedTypes do
                if typeInfo.Raw.IsGenericDefinition && not (isGenericSingleCaseWrapperDef typeInfo) then () else
                if emitPerTypeFiles then
                    let fileName =
                        match typeInfo.GenericContext with
                        | Some ctx ->
                            let rec argPascalName (ti: TypeInfo) =
                                if not ti.GenericArguments.IsEmpty then
                                    let argPart = ti.GenericArguments |> List.map argPascalName |> String.concat ""
                                    typeInfoToPascalName { ti with GenericArguments = [] } + argPart
                                else typeInfoToPascalName ti
                            let argNames = ctx.GenericArguments |> List.map argPascalName
                            sprintf "%s_%s" typeInfo.Raw.TypeName (String.concat "" argNames)
                        | None -> typeInfo.Raw.TypeName
                    let code = SerdeCodeEmitter.emit emitter typeInfo
                    generatedSources.Add({ HintName = sprintf "%s.%s.g.fs" fileName emitter.HintNameSuffix; Code = code; AbsolutePath = None })
                allTypes.Add(typeInfo)

            // Discover and emit option types from record fields
            let optionTypeInfos = OptionDiscovery.discoverOptionTypes allTypes
            for optTi in optionTypeInfos do
                let optSerdeInfo = OptionDiscovery.mkOptionSerdeTypeInfo optTi
                if emitPerTypeFiles then
                    let code = SerdeCodeEmitter.emit emitter optSerdeInfo
                    let pascalName = typeInfoToPascalName optTi
                    generatedSources.Add({ HintName = sprintf "%s.%s.g.fs" pascalName emitter.HintNameSuffix; Code = code; AbsolutePath = None })
                allTypes.Add(optSerdeInfo)

            // Discover and emit tuple types from record fields
            let tupleTypeInfos = TupleDiscovery.discoverTupleTypes allTypes
            for tupTi in tupleTypeInfos do
                let tupSerdeInfo = TupleDiscovery.mkTupleSerdeTypeInfo tupTi
                if emitPerTypeFiles then
                    let code = SerdeCodeEmitter.emit emitter tupSerdeInfo
                    let pascalName = typeInfoToPascalName tupTi
                    generatedSources.Add({ HintName = sprintf "%s.%s.g.fs" pascalName emitter.HintNameSuffix; Code = code; AbsolutePath = None })
                allTypes.Add(tupSerdeInfo)

            // Emit resolver file if the emitter supports it
            match emitter with
            | :? ISerdeResolverEmitter as resolverEmitter ->
                match resolverEmitter.EmitResolver(Seq.toList allTypes) with
                | Some code ->
                    generatedSources.Add({ HintName = resolverEmitter.ResolverHintName; Code = code; AbsolutePath = None })
                    for (hintName, code) in resolverEmitter.EmitRegistrationFiles() do
                        generatedSources.Add({ HintName = hintName; Code = code; AbsolutePath = None })
                | None -> ()
            | _ -> ()

            // Emit RPC dispatch modules for [<RpcApi>] interfaces
            if not rpcDiscoveryResult.Interfaces.IsEmpty then
                match emitter with
                | :? ISerdeRpcEmitter as rpcEmitter ->
                    for (hintName, code) in rpcEmitter.EmitRpcModules(rpcDiscoveryResult.Interfaces) do
                        generatedSources.Add({ HintName = hintName; Code = code; AbsolutePath = None })
                    let crossResult = rpcEmitter.EmitCrossProjectFiles(rpcDiscoveryResult.Interfaces, Seq.toList allTypes)
                    for (absPath, code) in crossResult.Files do
                        let hint = System.IO.Path.GetFileName(absPath)
                        generatedSources.Add({ HintName = hint; Code = code; AbsolutePath = Some absPath })
                    for err in crossResult.Errors do
                        errors.Add err
                | _ -> ()

            // Emit entry point wrapper only when (a) something else was generated for
            // this project (codecs, dispatch, resolver) — otherwise the wrapper has
            // nothing to bootstrap and would just force the user to add a module
            // declaration to Program.fs for no benefit — and (b) the local project's
            // own sources contain [<EntryPoint>]. Ref-source [<EntryPoint>] is for the
            // referenced project's executable, not ours.
            let isLocalSourceFile (path: string) =
                try
                    let abs = System.IO.Path.GetFullPath(path)
                    abs.StartsWith(projectDir, System.StringComparison.OrdinalIgnoreCase)
                with _ -> false

            let hasGeneratedOutput = generatedSources.Count > 0

            let emitEntryPoint =
                hasGeneratedOutput
                && sourceFiles
                   |> Seq.exists (fun (path, text) ->
                       path.EndsWith(".fs") &&
                       isLocalSourceFile path &&
                       EntryPointDetector.detect path text |> Option.isSome
                   )

            if emitEntryPoint then
                for (filePath, sourceText) in sourceFiles do
                    if filePath.EndsWith(".fs") && isLocalSourceFile filePath then
                        match EntryPointDetector.detect filePath sourceText with
                        | Some info ->
                            let info =
                                { info with
                                    BootstrapInterface = "Serde.FS.IEntryPointBootstrap"
                                    BootstrapRunner = Some "Serde.FS.Bootstrap.Run" }
                            let code = EntryPointEmitter.emit info
                            generatedSources.Add({ HintName = "~~EntryPoint.djinn.g.fs"; Code = code; AbsolutePath = None })
                        | None -> ()

        { Sources = Seq.toList generatedSources; Errors = Seq.toList errors; Warnings = Seq.toList warnings }
