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

    let private primitiveNames =
        set [
            "int"; "int8"; "int16"; "int32"; "int64"
            "uint8"; "uint16"; "uint32"; "uint64"
            "byte"; "sbyte"
            "string"; "bool"
            "float"; "double"; "single"; "float32"; "decimal"
            "unit"
            "Guid"; "System.Guid"
            "DateTime"; "System.DateTime"
            "DateTimeOffset"; "System.DateTimeOffset"
            "TimeSpan"; "System.TimeSpan"
            "DateOnly"; "System.DateOnly"
            "TimeOnly"; "System.TimeOnly"
        ]

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

    let private normalizePrimitive (name: string) =
        match name with
        | "System.Int32" | "int32" -> "int"
        | "System.Int64" -> "int64"
        | "System.String" -> "string"
        | "System.Boolean" -> "bool"
        | "System.Decimal" -> "decimal"
        | "System.Double" | "double" -> "float"
        | "System.Single" | "single" -> "float32"
        | "System.Byte" -> "byte"
        | "System.SByte" | "int8" -> "sbyte"
        | "System.Int16" -> "int16"
        | "System.UInt16" | "uint8" -> "byte"
        | "System.UInt32" -> "uint32"
        | "System.UInt64" -> "uint64"
        | other -> other

    /// Codec-module name built from a TypeInfo.
    let rec private codecModuleNameFromTi (ti: Types.TypeInfo) : string =
        if not ti.GenericArguments.IsEmpty then
            let argPart = ti.GenericArguments |> List.map codecModuleNameFromTi |> String.concat ""
            sanitize ti.TypeName + argPart + "Codec"
        else
            sanitize ti.TypeName + "Codec"

    let private fqnOfTypeInfo (ti: Types.TypeInfo) =
        let parts =
            [ yield! ti.Namespace |> Option.toList
              yield! ti.EnclosingModules
              yield ti.TypeName ]
        String.concat "." parts

    /// Convert a SourceDjinn TypeInfo into a FableTypeExpr.
    let rec private fromTypeInfo (ti: Types.TypeInfo) : FableTypeExpr =
        match ti.Kind with
        | Types.Primitive pk ->
            let n = primitiveKindToName pk
            if n = "unit" then FUnit else FPrim n
        | Types.Option inner -> FOption (fromTypeInfo inner)
        | Types.List inner -> FList (fromTypeInfo inner)
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

    // ── Parser for F# type-expression strings emitted by RpcApiDiscovery ────

    /// Split a generic argument list like "Product, string" at top-level commas,
    /// respecting nested < > pairs.
    let private splitTopLevelCommas (s: string) : string list =
        let result = ResizeArray<string>()
        let mutable depth = 0
        let mutable start = 0
        for i in 0 .. s.Length - 1 do
            match s.[i] with
            | '<' | '(' | '[' -> depth <- depth + 1
            | '>' | ')' | ']' -> depth <- depth - 1
            | ',' when depth = 0 ->
                result.Add(s.Substring(start, i - start))
                start <- i + 1
            | _ -> ()
        result.Add(s.Substring(start))
        result |> List.ofSeq |> List.map (fun s -> s.Trim())

    /// Split at top-level '*' (for tuple types) respecting nested delimiters.
    let private splitTopLevelStars (s: string) : string list =
        let result = ResizeArray<string>()
        let mutable depth = 0
        let mutable i = 0
        let mutable start = 0
        while i < s.Length do
            let c = s.[i]
            match c with
            | '<' | '(' | '[' -> depth <- depth + 1
            | '>' | ')' | ']' -> depth <- depth - 1
            | '*' when depth = 0 ->
                result.Add(s.Substring(start, i - start))
                start <- i + 1
            | _ -> ()
            i <- i + 1
        result.Add(s.Substring(start))
        result |> List.ofSeq |> List.map (fun s -> s.Trim())

    /// Resolve an atom identifier (possibly FQN) into a FableTypeExpr.
    /// types : lookup from FQN → SerdeTypeInfo (for user types).
    let private resolveAtom (lookup: Map<string, SerdeTypeInfo>) (name: string) : FableTypeExpr =
        let name = name.Trim()
        if name = "unit" then FUnit
        elif primitiveNames.Contains name then FPrim (normalizePrimitive name)
        else
            match Map.tryFind name lookup with
            | Some sti ->
                FUser (codecModuleNameFromTi sti.Raw, Types.typeInfoToFqFSharpType sti.Raw)
            | None ->
                // Fallback: treat as user type with synthesized codec name.
                // This covers case where user type isn't in our types map
                // (e.g., module-local types not walked).
                FUser (sanitize name + "Codec", name)

    /// Parse an F# type expression string into a FableTypeExpr.
    let rec private parseTypeString (lookup: Map<string, SerdeTypeInfo>) (raw: string) : FableTypeExpr =
        let s = raw.Trim()
        if s.Length = 0 then FUnknown raw
        // Strip enclosing parentheses
        elif s.StartsWith("(") && s.EndsWith(")") then
            parseTypeString lookup (s.Substring(1, s.Length - 2))
        else
            // Tuple: "A * B * C" at top level
            let tupleParts = splitTopLevelStars s
            if tupleParts.Length > 1 then
                FTuple (tupleParts |> List.map (parseTypeString lookup))
            // Array: "T[]"
            elif s.EndsWith("[]") then
                FArray (parseTypeString lookup (s.Substring(0, s.Length - 2)))
            else
                // Postfix suffix: "T option", "T list", "T array", "T seq"
                let postfix = [ " option"; " list"; " array"; " seq" ]
                let matched =
                    postfix |> List.tryFind (fun p -> s.EndsWith(p))
                match matched with
                | Some " option" -> FOption (parseTypeString lookup (s.Substring(0, s.Length - 7)))
                | Some " list" -> FList (parseTypeString lookup (s.Substring(0, s.Length - 5)))
                | Some " array" -> FArray (parseTypeString lookup (s.Substring(0, s.Length - 6)))
                | Some " seq" -> FList (parseTypeString lookup (s.Substring(0, s.Length - 4)))
                | _ ->
                    // Generic: Foo<A, B>  (only top-level < ... >)
                    let ltIdx = s.IndexOf('<')
                    if ltIdx > 0 && s.EndsWith(">") then
                        let head = s.Substring(0, ltIdx).Trim()
                        let inner = s.Substring(ltIdx + 1, s.Length - ltIdx - 2)
                        let args = splitTopLevelCommas inner |> List.map (parseTypeString lookup)
                        match head, args with
                        | "Result", [ ok; err ] -> FResult (ok, err)
                        | "option", [ inner ] | "Option", [ inner ] -> FOption inner
                        | "list", [ inner ] | "List", [ inner ] -> FList inner
                        | "array", [ inner ] | "Array", [ inner ] -> FArray inner
                        | "seq", [ inner ] | "Seq", [ inner ] -> FList inner
                        | "Set", [ inner ] -> FSet inner
                        | "Map", [ k; v ] -> FMap (k, v)
                        | _ ->
                            // Try to look up the generic head in the types map
                            let argFsTypes =
                                args
                                |> List.map (fun a ->
                                    match a with
                                    | FPrim p -> p
                                    | FUser (_, t) -> t
                                    | _ -> "obj")
                            let fqType = sprintf "%s<%s>" head (String.concat ", " argFsTypes)
                            let codecName =
                                let argCodecs =
                                    args
                                    |> List.map (fun a ->
                                        match a with
                                        | FPrim p -> sanitize p
                                        | FUser (n, _) -> n.Replace("Codec", "")
                                        | _ -> "obj")
                                    |> String.concat ""
                                sanitize head + argCodecs + "Codec"
                            FUser (codecName, fqType)
                    else
                        resolveAtom lookup s

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
        | FList inner | FArray inner | FSet inner ->
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

    /// Build an FQN → SerdeTypeInfo map for string-based type lookup.
    let private buildTypeLookup (types: SerdeTypeInfo list) : Map<string, SerdeTypeInfo> =
        types
        |> List.filter (fun t ->
            match t.Raw.Kind with
            | Types.Record _ | Types.AnonymousRecord _ | Types.Union _ | Types.Enum _ -> true
            | _ -> false)
        |> List.collect (fun t ->
            let fqn = fqnOfTypeInfo t.Raw
            let short = t.Raw.TypeName
            [ fqn, t; short, t ])
        |> List.distinctBy fst
        |> Map.ofList

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
        let lookup = buildTypeLookup types

        // Split interface FullName into (namespace, short name). If no namespace,
        // emit a placeholder namespace so the file is valid under .NET.
        let nsPart, _ =
            let lastDot = iface.FullName.LastIndexOf('.')
            if lastDot > 0 then iface.FullName.Substring(0, lastDot), iface.ShortName
            else "SerdeGenerated.Fable", iface.ShortName
        let fileModuleName = sprintf "%sFableClient" iface.ShortName

        // ── Body builder (codecs + client proxy) emitted at 0 indent, later
        //    wrapped in a module rec under the namespace.
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
        bappend (sprintf "    { new %s with" iface.FullName)
        for m in iface.Methods do
            let outputTy = parseTypeString lookup m.OutputType
            if m.InputType = "unit" then
                bappend (sprintf "        member _.%s() =" m.MethodName)
                bappend          "            async {"
                bappend (sprintf "                let url = fullUrl \"%s\"" m.MethodName)
                bappend          "                let! respText = Interop.postJson url \"null\" |> Async.AwaitPromise"
                bappend          "                let json = Interop.parse respText"
                bappend (sprintf "                return %s" (decodeExpr "json" outputTy))
                bappend          "            }"
            else
                let inputTy = parseTypeString lookup m.InputType
                bappend (sprintf "        member _.%s(arg: %s) =" m.MethodName m.InputType)
                bappend          "            async {"
                bappend (sprintf "                let url = fullUrl \"%s\"" m.MethodName)
                bappend (sprintf "                let body = Interop.stringify (%s)" (encodeExpr "arg" inputTy))
                bappend          "                let! respText = Interop.postJson url body |> Async.AwaitPromise"
                bappend          "                let json = Interop.parse respText"
                bappend (sprintf "                return %s" (decodeExpr "json" outputTy))
                bappend          "            }"
        bappend          "    }"

        let indentedBody = indentBlock "    " (body.ToString())

        // ── Final file layout ──────────────────────────────────────
        let out = StringBuilder()
        let oappend (s: string) = out.Append(s).Append('\n') |> ignore

        oappend "// <auto-generated />"
        oappend (sprintf "namespace %s" nsPart)
        oappend ""
        oappend "#if FABLE_COMPILER"
        oappend ""
        oappend "open Fable.Core"
        oappend "open Fable.Core.JsInterop"
        oappend ""
        oappend (sprintf "module rec %s =" fileModuleName)
        oappend ""
        out.Append(indentedBody) |> ignore
        oappend ""
        oappend "#endif"

        out.ToString()

    /// Compute the absolute path where the Fable file should be written.
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
                    Path.Combine(sourceDir, "obj", "serde-generated", "fable")
            let fileName = sprintf "%s.fs" iface.ShortName
            Some (Path.Combine(outDir, fileName))
