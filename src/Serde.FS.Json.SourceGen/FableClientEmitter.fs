namespace Serde.FS.Json.SourceGen

open System.Text
open System.IO
open Serde.FS
open FSharp.SourceDjinn.TypeModel

/// Emits a self-contained Fable-compatible F# client for an [<RpcApi>] interface
/// that has been annotated with [<GenerateFableClient>]. The emitted file is
/// guarded with #if FABLE_COMPILER so it is inert under .NET compilation.
module internal FableClientEmitter =

    /// Minimal type expression tree used by the Fable emitter.
    type FableTypeExpr =
        | FPrim of string
        | FOption of FableTypeExpr
        | FList of FableTypeExpr
        /// `seq<T>` — wire format identical to FList but the decoded value
        /// must be a seq (not a list) so F# doesn't reject the assignment in
        /// strict contexts like record-field annotations and member overrides.
        | FSeq of FableTypeExpr
        | FArray of FableTypeExpr
        | FSet of FableTypeExpr
        | FMap of FableTypeExpr * FableTypeExpr
        | FTuple of FableTypeExpr list
        | FResult of FableTypeExpr * FableTypeExpr
        | FAsync of FableTypeExpr
        | FUnit
        /// A named user type. The string is the pascal codec-module name
        /// (after sanitization) and the second field is the F# type expression
        /// used in type annotations.
        | FUser of codecName: string * fsharpType: string
        | FUnknown of string

    /// Canonical short string for a PrimitiveKind case.
    let private primitiveKindToName (pk: Types.PrimitiveKind) : string =
        match pk with
        | Types.PrimitiveKind.Unit -> "unit"
        | Types.PrimitiveKind.Bool -> "bool"
        | Types.PrimitiveKind.Int8 -> "sbyte"
        | Types.PrimitiveKind.Int16 -> "int16"
        | Types.PrimitiveKind.Int32 -> "int"
        | Types.PrimitiveKind.Int64 -> "int64"
        | Types.PrimitiveKind.UInt8 -> "byte"
        | Types.PrimitiveKind.UInt16 -> "uint16"
        | Types.PrimitiveKind.UInt32 -> "uint32"
        | Types.PrimitiveKind.UInt64 -> "uint64"
        | Types.PrimitiveKind.Float32 -> "float32"
        | Types.PrimitiveKind.Float64 -> "float"
        | Types.PrimitiveKind.Decimal -> "decimal"
        | Types.PrimitiveKind.String -> "string"
        | Types.PrimitiveKind.Guid -> "Guid"
        | Types.PrimitiveKind.DateTime -> "DateTime"
        | Types.PrimitiveKind.DateTimeOffset -> "DateTimeOffset"
        | Types.PrimitiveKind.TimeSpan -> "TimeSpan"
        | Types.PrimitiveKind.DateOnly -> "DateOnly"
        | Types.PrimitiveKind.TimeOnly -> "TimeOnly"

    let private sanitize (name: string) =
        name
            .Replace(".", "_")
            .Replace("+", "_")
            .Replace("`", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace("[", "_")
            .Replace("]", "_")
            .Replace(" ", "")

    /// Codec-module name built from a TypeInfo.
    let rec private codecModuleNameFromTi (ti: Types.TypeInfo) : string =
        if not ti.GenericArguments.IsEmpty then
            let argPart = ti.GenericArguments |> List.map codecModuleNameFromTi |> String.concat ""
            sanitize ti.TypeName + argPart + "Codec"
        else
            sanitize ti.TypeName + "Codec"

    /// Convert a SourceDjinn TypeInfo into a FableTypeExpr.
    let rec private fromTypeInfo (ti: Types.TypeInfo) : FableTypeExpr =
        match ti.Kind with
        | Types.Primitive pk ->
            let n = primitiveKindToName pk
            if n = "unit" then FUnit else FPrim n
        | Types.Option inner -> FOption (fromTypeInfo inner)
        | Types.List inner ->
            // synTypeToTypeInfo collapses `seq<T>` onto TypeKind.List for wire
            // compatibility but preserves TypeName="seq" so we can distinguish
            // here. List → FList, seq → FSeq (decodes to seq, not list).
            if ti.TypeName = "seq" then FSeq (fromTypeInfo inner)
            else FList (fromTypeInfo inner)
        | Types.Array inner -> FArray (fromTypeInfo inner)
        | Types.Set inner -> FSet (fromTypeInfo inner)
        | Types.Map (k, v) -> FMap (fromTypeInfo k, fromTypeInfo v)
        | Types.Tuple fields -> FTuple (fields |> List.map (fun f -> fromTypeInfo f.Type))
        | Types.ConstructedGenericType when ti.TypeName = "Result" && ti.GenericArguments.Length = 2 ->
            FResult (fromTypeInfo ti.GenericArguments.[0], fromTypeInfo ti.GenericArguments.[1])
        | Types.ConstructedGenericType ->
            FUser (codecModuleNameFromTi ti, Types.typeInfoToFqFSharpType ti)
        | Types.Record _ | Types.AnonymousRecord _ | Types.Union _ | Types.Enum _ ->
            FUser (codecModuleNameFromTi ti, Types.typeInfoToFqFSharpType ti)
        | _ -> FUnknown (Types.typeInfoToFqFSharpType ti)

    // ── Encoder / decoder expression generation ─────────────────────────────

    let rec private encodeExpr (varExpr: string) (ty: FableTypeExpr) : string =
        match ty with
        | FUnit -> "null"
        | FPrim name ->
            match name with
            | "int" | "int32" | "int16" | "int8" | "sbyte" | "byte"
            | "uint16" | "uint32"
            | "bool" | "string"
            | "float" | "double" ->
                sprintf "box (%s)" varExpr
            | "int64" | "uint64" | "float32" | "single" | "decimal" ->
                sprintf "box (float (%s))" varExpr
            | "Guid" | "System.Guid" ->
                sprintf "box (string (%s))" varExpr
            | "DateTime" | "System.DateTime"
            | "DateTimeOffset" | "System.DateTimeOffset"
            | "DateOnly" | "System.DateOnly"
            | "TimeOnly" | "System.TimeOnly" ->
                sprintf "box ((%s).ToString(\"O\"))" varExpr
            | _ -> sprintf "box (%s)" varExpr
        | FOption inner ->
            sprintf "(match %s with | Some x -> %s | None -> null)" varExpr (encodeExpr "x" inner)
        | FList inner | FArray inner | FSet inner | FSeq inner ->
            sprintf "(%s |> Seq.map (fun x -> %s) |> Array.ofSeq |> box)" varExpr (encodeExpr "x" inner)
        | FMap _ ->
            "(failwith \"Map encoding not supported by Fable client generator\")"
        | FTuple elements ->
            let pattern = elements |> List.mapi (fun i _ -> sprintf "e%d" i) |> String.concat ", "
            let encs =
                elements
                |> List.mapi (fun i t -> encodeExpr (sprintf "e%d" i) t)
                |> String.concat "; "
            sprintf "(let (%s) = %s in box [| %s |])" pattern varExpr encs
        | FResult (ok, err) ->
            let okE = encodeExpr "x" ok
            let errE = encodeExpr "x" err
            sprintf "(match %s with | Ok x -> createObj [ \"Ok\", %s ] | Error x -> createObj [ \"Error\", %s ])" varExpr okE errE
        | FAsync _ -> sprintf "box (%s)" varExpr
        | FUser (codecName, _) -> sprintf "%s.encode (%s)" codecName varExpr
        | FUnknown _ -> sprintf "box (%s)" varExpr

    let rec private decodeExpr (jsonExpr: string) (ty: FableTypeExpr) : string =
        match ty with
        | FUnit -> "()"
        | FPrim name ->
            match name with
            | "int" | "int32" -> sprintf "(unbox<int> %s)" jsonExpr
            | "int16" -> sprintf "(int16 (unbox<float> %s))" jsonExpr
            | "int8" | "sbyte" -> sprintf "(sbyte (unbox<float> %s))" jsonExpr
            | "byte" -> sprintf "(byte (unbox<float> %s))" jsonExpr
            | "uint16" -> sprintf "(uint16 (unbox<float> %s))" jsonExpr
            | "uint32" -> sprintf "(uint32 (unbox<float> %s))" jsonExpr
            | "uint64" -> sprintf "(uint64 (unbox<float> %s))" jsonExpr
            | "int64" -> sprintf "(int64 (unbox<float> %s))" jsonExpr
            | "string" -> sprintf "(unbox<string> %s)" jsonExpr
            | "bool" -> sprintf "(unbox<bool> %s)" jsonExpr
            | "float" | "double" -> sprintf "(unbox<float> %s)" jsonExpr
            | "float32" | "single" -> sprintf "(float32 (unbox<float> %s))" jsonExpr
            | "decimal" -> sprintf "(decimal (unbox<float> %s))" jsonExpr
            | "Guid" | "System.Guid" -> sprintf "(System.Guid.Parse (unbox<string> %s))" jsonExpr
            | "DateTime" | "System.DateTime" -> sprintf "(System.DateTime.Parse (unbox<string> %s))" jsonExpr
            | "DateTimeOffset" | "System.DateTimeOffset" -> sprintf "(System.DateTimeOffset.Parse (unbox<string> %s))" jsonExpr
            | "DateOnly" | "System.DateOnly" -> sprintf "(System.DateOnly.Parse (unbox<string> %s))" jsonExpr
            | "TimeOnly" | "System.TimeOnly" -> sprintf "(System.TimeOnly.Parse (unbox<string> %s))" jsonExpr
            | _ -> sprintf "(unbox %s)" jsonExpr
        | FOption inner ->
            sprintf "(let v = %s in if Interop.isNullish v then None else Some (%s))" jsonExpr (decodeExpr "v" inner)
        | FList inner ->
            sprintf "(unbox<obj[]> %s |> Array.map (fun x -> %s) |> Array.toList)" jsonExpr (decodeExpr "x" inner)
        | FSeq inner ->
            // Wire shape is identical to FList, but the consuming F# context
            // expects a seq<T>, not a list<T>. F# doesn't auto-coerce list to
            // seq in record-field init / interface member overrides.
            sprintf "(unbox<obj[]> %s |> Array.map (fun x -> %s) :> seq<_>)" jsonExpr (decodeExpr "x" inner)
        | FArray inner ->
            sprintf "(unbox<obj[]> %s |> Array.map (fun x -> %s))" jsonExpr (decodeExpr "x" inner)
        | FSet inner ->
            sprintf "(unbox<obj[]> %s |> Array.map (fun x -> %s) |> Set.ofArray)" jsonExpr (decodeExpr "x" inner)
        | FMap _ ->
            "(failwith \"Map decoding not supported by Fable client generator\")"
        | FTuple elements ->
            let decs =
                elements
                |> List.mapi (fun i t ->
                    decodeExpr (sprintf "((unbox<obj[]> %s).[%d])" jsonExpr i) t)
                |> String.concat ", "
            sprintf "(%s)" decs
        | FResult (ok, err) ->
            let okD = decodeExpr "v" ok
            let errD = decodeExpr "v" err
            sprintf "(let o = %s in if Interop.hasKey o \"Ok\" then Ok (let v = o?Ok in %s) else Error (let v = o?Error in %s))" jsonExpr okD errD
        | FAsync _ -> sprintf "(unbox %s)" jsonExpr
        | FUser (codecName, _) -> sprintf "(%s.decode %s)" codecName jsonExpr
        | FUnknown _ -> sprintf "(unbox %s)" jsonExpr

    // ── Codec module emission per Serde type ────────────────────────────────

    let private emitRecordCodec (sb: StringBuilder) (info: SerdeTypeInfo) =
        let append (s: string) = sb.Append(s).Append('\n') |> ignore
        let fields = info.Fields |> Option.defaultValue []
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let modName = codecModuleNameFromTi info.Raw

        append (sprintf "module private %s =" modName)
        append (sprintf "    let encode (value: %s) : obj =" fqType)
        append          "        createObj ["
        for field in fields do
            let expr = encodeExpr (sprintf "value.%s" field.RawName) (fromTypeInfo field.Type)
            append (sprintf "            \"%s\", %s" field.Name expr)
        append          "        ]"
        append (sprintf "    let decode (json: obj) : %s =" fqType)
        append          "        {"
        for field in fields do
            let expr = decodeExpr (sprintf "(json?(\"%s\"))" field.Name) (fromTypeInfo field.Type)
            append (sprintf "            %s = %s" field.RawName expr)
        append          "        }"
        append ""

    let private emitWrapperUnionCodec (sb: StringBuilder) (info: SerdeTypeInfo) =
        let append (s: string) = sb.Append(s).Append('\n') |> ignore
        let cases = info.UnionCases |> Option.defaultValue []
        let case = cases.[0]
        let field = case.Fields.[0]
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let modName = codecModuleNameFromTi info.Raw

        append (sprintf "module private %s =" modName)
        append (sprintf "    let encode (value: %s) : obj =" fqType)
        append          "        match value with"
        append (sprintf "        | %s.%s x ->" fqType case.RawCaseName)
        append (sprintf "            createObj [ \"%s\", %s ]" case.CaseName (encodeExpr "x" (fromTypeInfo field.Type)))
        append (sprintf "    let decode (json: obj) : %s =" fqType)
        append (sprintf "        let v = json?(\"%s\")" case.CaseName)
        append (sprintf "        %s.%s (%s)" fqType case.RawCaseName (decodeExpr "v" (fromTypeInfo field.Type)))
        append ""

    let private emitMultiCaseUnionCodec (sb: StringBuilder) (info: SerdeTypeInfo) =
        let append (s: string) = sb.Append(s).Append('\n') |> ignore
        let cases =
            info.UnionCases
            |> Option.defaultValue []
            |> List.filter (fun c -> not c.Attributes.Skip)
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let modName = codecModuleNameFromTi info.Raw

        append (sprintf "module private %s =" modName)
        append (sprintf "    let encode (value: %s) : obj =" fqType)
        append          "        match value with"
        for case in cases do
            match case.Fields with
            | [] ->
                append (sprintf "        | %s.%s -> createObj [ \"Case\", box \"%s\"; \"Fields\", box [||] ]" fqType case.RawCaseName case.CaseName)
            | fields ->
                let args = fields |> List.mapi (fun i _ -> sprintf "e%d" i) |> String.concat ", "
                let encs =
                    fields
                    |> List.mapi (fun i f -> encodeExpr (sprintf "e%d" i) (fromTypeInfo f.Type))
                    |> String.concat "; "
                append (sprintf "        | %s.%s(%s) ->" fqType case.RawCaseName args)
                append (sprintf "            createObj [ \"Case\", box \"%s\"; \"Fields\", box [| %s |] ]" case.CaseName encs)
        append ""
        append (sprintf "    let decode (json: obj) : %s =" fqType)
        append          "        let caseName = unbox<string> (json?Case)"
        append          "        let fieldsArr = unbox<obj[]> (json?Fields)"
        for i, case in cases |> List.mapi (fun i c -> i, c) do
            let keyword = if i = 0 then "if" else "elif"
            match case.Fields with
            | [] ->
                append (sprintf "        %s caseName = \"%s\" then %s.%s" keyword case.CaseName fqType case.RawCaseName)
            | fields ->
                let decs =
                    fields
                    |> List.mapi (fun j f ->
                        decodeExpr (sprintf "(fieldsArr.[%d])" j) (fromTypeInfo f.Type))
                    |> String.concat ", "
                append (sprintf "        %s caseName = \"%s\" then %s.%s(%s)" keyword case.CaseName fqType case.RawCaseName decs)
        append          "        else failwith (sprintf \"Unknown union case: %s\" caseName)"
        append ""

    let private emitEnumCodec (sb: StringBuilder) (info: SerdeTypeInfo) =
        let append (s: string) = sb.Append(s).Append('\n') |> ignore
        let cases =
            info.EnumCases
            |> Option.defaultValue []
            |> List.filter (fun c -> not c.Attributes.Skip)
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let modName = codecModuleNameFromTi info.Raw

        append (sprintf "module private %s =" modName)
        append (sprintf "    let encode (value: %s) : obj =" fqType)
        for i, c in cases |> List.mapi (fun i c -> i, c) do
            let keyword = if i = 0 then "if" else "elif"
            append (sprintf "        %s value = %s.%s then box \"%s\"" keyword fqType c.RawCaseName c.CaseName)
        append          "        else failwith (sprintf \"Unknown enum value: %A\" value)"
        append (sprintf "    let decode (json: obj) : %s =" fqType)
        append          "        let s = unbox<string> json"
        for i, c in cases |> List.mapi (fun i c -> i, c) do
            let keyword = if i = 0 then "if" else "elif"
            append (sprintf "        %s s = \"%s\" then %s.%s" keyword c.CaseName fqType c.RawCaseName)
        append          "        else failwith (sprintf \"Unknown enum value: %s\" s)"
        append ""

    let private emitTypeCodec (sb: StringBuilder) (info: SerdeTypeInfo) =
        match info.Raw.Kind with
        | Types.Record _ | Types.AnonymousRecord _ ->
            emitRecordCodec sb info
        | Types.Union _ ->
            let cases = info.UnionCases |> Option.defaultValue []
            match cases with
            | [single] when single.Fields.Length = 1 -> emitWrapperUnionCodec sb info
            | _ -> emitMultiCaseUnionCodec sb info
        | Types.Enum _ ->
            emitEnumCodec sb info
        | _ -> ()

    let private shouldEmitCodec (info: SerdeTypeInfo) : bool =
        if info.Raw.IsGenericDefinition then false
        else
            match info.Raw.Kind with
            | Types.Record _ | Types.AnonymousRecord _ | Types.Union _ | Types.Enum _ -> true
            | _ -> false

    let private computeBasePath (iface: RpcInterfaceInfo) =
        let root = iface.Root |> Option.defaultValue iface.ShortName
        let version =
            match iface.Version with
            | Some v when v.Length > 0 -> "/" + v
            | _ -> ""
        "/rpc/" + root + version

    /// Indent every non-empty line of `body` by `prefix`.
    let private indentBlock (prefix: string) (body: string) : string =
        let lines = body.Split([| '\n' |])
        let rebuilt =
            lines
            |> Array.map (fun line ->
                if line.Length = 0 then line
                else prefix + line)
        String.concat "\n" rebuilt

    /// Emit the full Fable client file for one interface.
    let emit (iface: RpcInterfaceInfo) (types: SerdeTypeInfo list) : string =
        let basePath = computeBasePath iface

        // Split interface FullName into (parent path, short name).
        let parentPath =
            let lastDot = iface.FullName.LastIndexOf('.')
            if lastDot > 0 then iface.FullName.Substring(0, lastDot)
            else ""
        let fileModuleName = sprintf "%sFableClient" iface.ShortName

        // ── Body builder (codecs + client proxy) emitted at 0 indent, later
        //    either indented under `module rec X = ...` (namespace case) or used
        //    verbatim as the body of a top-level `module rec X.Y.Z` (module case).
        let body = StringBuilder()
        let bappend (s: string) = body.Append(s).Append('\n') |> ignore

        // Interop helpers
        bappend "module private Interop ="
        bappend "    [<Emit(\"JSON.stringify($0)\")>]"
        bappend "    let stringify (x: obj) : string = jsNative"
        bappend ""
        bappend "    [<Emit(\"JSON.parse($0)\")>]"
        bappend "    let parse (s: string) : obj = jsNative"
        bappend ""
        bappend "    [<Emit(\"$0 == null\")>]"
        bappend "    let isNullish (x: obj) : bool = jsNative"
        bappend ""
        bappend "    [<Emit(\"Object.prototype.hasOwnProperty.call($0, $1)\")>]"
        bappend "    let hasKey (x: obj) (k: string) : bool = jsNative"
        bappend ""
        bappend "    [<Emit(\"fetch($0, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: $1 }).then(function (r) { if (!r.ok) { throw new Error('HTTP ' + r.status + ' at ' + $0); } return r.text(); })\")>]"
        bappend "    let postJson (url: string) (body: string) : JS.Promise<string> = jsNative"
        bappend ""

        // Generated codecs
        for info in types do
            if shouldEmitCodec info then
                emitTypeCodec body info

        // Client proxy
        bappend (sprintf "let create (baseUrl: string) : %s =" iface.FullName)
        bappend          "    let trimmed = baseUrl.TrimEnd('/')"
        bappend          "    let fullUrl (methodName: string) ="
        if iface.UrlCaseValue = 1 then
            bappend      "        let seg ="
            bappend      "            let chars ="
            bappend      "                methodName"
            bappend      "                |> Seq.collect (fun c ->"
            bappend      "                    if System.Char.IsUpper c then seq { '-'; System.Char.ToLowerInvariant c }"
            bappend      "                    else seq { c })"
            bappend      "                |> Seq.toArray"
            bappend      "            let s = System.String(chars)"
            bappend      "            if s.StartsWith(\"-\") then s.Substring(1) else s"
            bappend (sprintf "        trimmed + \"%s/\" + seg" basePath)
        else
            bappend (sprintf "        trimmed + \"%s/\" + methodName" basePath)
        // Resolve a method-level type using the structural TypeInfo populated
        // by discovery. This is the single source of truth for codec naming —
        // emitter-side `codecModuleNameFromTi` and the encode/decode expression
        // generator both operate on the same TypeInfo so naming cannot drift.
        // Step 6 will replace the fallback `failwith` with a clickable MSBuild
        // diagnostic surfaced via the host's stderr.
        let resolveTy (typeInfo: Types.TypeInfo option) (typeString: string) : FableTypeExpr =
            match typeInfo with
            | Some ti -> fromTypeInfo ti
            | None ->
                failwithf
                    "[Serde.FS] FableClientEmitter: missing TypeInfo for '%s' in interface '%s'. \
                     Discovery did not resolve this type — likely because the type isn't declared \
                     in a project source file walked by Serde."
                    typeString iface.FullName

        // Render a parameter type annotation. Discovery's raw string returns
        // the unqualified name for System.* primitives (e.g. "DateTime"), which
        // doesn't resolve in the generated file because we don't `open System`.
        // For DateTime-family primitives, emit the System-qualified form.
        let systemQualified (pk: Types.PrimitiveKind) : string option =
            match pk with
            | Types.PrimitiveKind.Guid -> Some "System.Guid"
            | Types.PrimitiveKind.DateTime -> Some "System.DateTime"
            | Types.PrimitiveKind.DateTimeOffset -> Some "System.DateTimeOffset"
            | Types.PrimitiveKind.TimeSpan -> Some "System.TimeSpan"
            | Types.PrimitiveKind.DateOnly -> Some "System.DateOnly"
            | Types.PrimitiveKind.TimeOnly -> Some "System.TimeOnly"
            | _ -> None

        let annotation (tiOpt: Types.TypeInfo option) (fallbackStr: string) =
            match tiOpt with
            | Some ti ->
                match ti.Kind with
                | Types.Primitive pk ->
                    match systemQualified pk with
                    | Some s -> s
                    | None -> Types.typeInfoToFqFSharpType ti
                | _ -> Types.typeInfoToFqFSharpType ti
            | None -> fallbackStr

        bappend (sprintf "    { new %s with" iface.FullName)
        for m in iface.Methods do
            let outputTy = resolveTy m.OutputTypeInfo m.OutputType
            if m.InputType = "unit" then
                bappend (sprintf "        member _.%s() =" m.MethodName)
                bappend          "            async {"
                bappend (sprintf "                let url = fullUrl \"%s\"" m.MethodName)
                bappend          "                let! respText = Interop.postJson url \"null\" |> Async.AwaitPromise"
                bappend          "                let json = Interop.parse respText"
                bappend (sprintf "                return %s" (decodeExpr "json" outputTy))
                bappend          "            }"
            elif m.InputIsTupled then
                // Multi-arg interface methods: F# treats `abstract Foo: A * B -> C` as a
                // 2-arg method, so the override must use multi-arg syntax. We encode each
                // parameter individually and combine into the JSON tuple-array wire format.
                // Match per-param TypeInfos (populated by discovery) against the
                // per-param strings positionally. The lists are the same length
                // when discovery succeeded; padding with None preserves the
                // fallback behaviour otherwise.
                let paramTyInfos =
                    if m.InputParamTypeInfos.Length = m.InputParams.Length then
                        m.InputParamTypeInfos
                    else
                        List.replicate m.InputParams.Length None
                let paramSig =
                    List.zip m.InputParams paramTyInfos
                    |> List.mapi (fun i (ty, tiOpt) -> sprintf "p%d: %s" i (annotation tiOpt ty))
                    |> String.concat ", "
                let encodedArgs =
                    List.zip m.InputParams paramTyInfos
                    |> List.mapi (fun i (ty, tiOpt) ->
                        let pTy = resolveTy tiOpt ty
                        encodeExpr (sprintf "p%d" i) pTy)
                    |> String.concat "; "
                bappend (sprintf "        member _.%s(%s) =" m.MethodName paramSig)
                bappend          "            async {"
                bappend (sprintf "                let url = fullUrl \"%s\"" m.MethodName)
                bappend (sprintf "                let body = Interop.stringify (box [| %s |])" encodedArgs)
                bappend          "                let! respText = Interop.postJson url body |> Async.AwaitPromise"
                bappend          "                let json = Interop.parse respText"
                bappend (sprintf "                return %s" (decodeExpr "json" outputTy))
                bappend          "            }"
            else
                let inputTy = resolveTy m.InputTypeInfo m.InputType
                bappend (sprintf "        member _.%s(arg: %s) =" m.MethodName (annotation m.InputTypeInfo m.InputType))
                bappend          "            async {"
                bappend (sprintf "                let url = fullUrl \"%s\"" m.MethodName)
                bappend (sprintf "                let body = Interop.stringify (%s)" (encodeExpr "arg" inputTy))
                bappend          "                let! respText = Interop.postJson url body |> Async.AwaitPromise"
                bappend          "                let json = Interop.parse respText"
                bappend (sprintf "                return %s" (decodeExpr "json" outputTy))
                bappend          "            }"
        bappend          "    }"

        let bodyText = body.ToString()

        // ── Final file layout ──────────────────────────────────────
        // Two shapes depending on how the user declared their [<RpcApi>] host file:
        //
        //  (a) `namespace Foo.Bar` (multi-file namespace):
        //        namespace Foo.Bar
        //        module rec IServerApiFableClient = ...
        //      User opens Foo.Bar — gets both interface and client.
        //
        //  (b) `module Foo.Bar` (top-level single-file module):
        //        module rec Foo.IServerApiFableClient = ...
        //      The client lives as a sibling top-level module under implicit
        //      namespace `Foo`. User opens both `Foo.Bar` (for interface) and
        //      `Foo` (for client), or fully-qualifies `Foo.IServerApiFableClient`.
        //      We cannot extend `module Foo.Bar` from a separate file (F#
        //      disallows multi-file additions to a top-level module), hence the
        //      sibling fallback.
        let out = StringBuilder()
        let oappend (s: string) = out.Append(s).Append('\n') |> ignore

        oappend "// <auto-generated />"

        if iface.IsParentNamespace then
            // (a) namespace
            oappend (sprintf "namespace %s" parentPath)
            oappend ""
            oappend "open Fable.Core"
            oappend "open Fable.Core.JsInterop"
            oappend ""
            oappend (sprintf "module rec %s =" fileModuleName)
            oappend ""
            out.Append(indentBlock "    " bodyText) |> ignore
            oappend ""
        else
            // (b) top-level module — emit as sibling module under the parent's parent.
            // Drop the last segment of `parentPath` (the user's module name) to get
            // the implicit namespace path, then attach our generated module name.
            let siblingParent =
                let lastDot = parentPath.LastIndexOf('.')
                if lastDot > 0 then parentPath.Substring(0, lastDot) else ""
            let modulePath =
                if siblingParent = "" then fileModuleName
                else sprintf "%s.%s" siblingParent fileModuleName

            oappend (sprintf "module rec %s" modulePath)
            oappend ""
            oappend "open Fable.Core"
            oappend "open Fable.Core.JsInterop"
            oappend ""
            out.Append(bodyText) |> ignore
            oappend ""

        out.ToString()

    /// Compute the absolute path where the Fable file should be written.
    /// The default location is "<SharedProjectDir>/generated-fable/<ApiName>.fs".
    /// We deliberately avoid obj/ because Fable's project cracker filters out
    /// any file whose path contains /obj/ — see Fable's removeFilesInObjFolder.
    let resolveOutputPath (iface: RpcInterfaceInfo) : string option =
        match iface.SourceFilePath with
        | None -> None
        | Some sourceFile ->
            let sourceDir = Path.GetDirectoryName(Path.GetFullPath sourceFile)
            let outDir =
                match iface.FableOutputDir with
                | Some custom when not (System.String.IsNullOrWhiteSpace custom) ->
                    if Path.IsPathRooted custom then custom
                    else Path.GetFullPath(Path.Combine(sourceDir, custom))
                | _ ->
                    Path.Combine(sourceDir, "generated-fable")
            let fileName = sprintf "%s.fs" iface.ShortName
            Some (Path.Combine(outDir, fileName))
