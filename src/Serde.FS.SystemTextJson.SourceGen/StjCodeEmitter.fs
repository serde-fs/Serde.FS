namespace Serde.FS.SystemTextJson.SourceGen

open System.Text
open Serde.FS
open FSharp.SourceDjinn.TypeModel

module internal StjCodeEmitterImpl =

    let writerCallForType (fieldName: string) (normalizedType: string) (valueExpr: string) (indent: string) : string =
        match normalizedType with
        | "string" ->
            $"%s{indent}writer.WriteString(\"%s{fieldName}\", %s{valueExpr})"
        | "int" | "int32" | "int64" | "float" | "double" | "decimal" ->
            $"%s{indent}writer.WriteNumber(\"%s{fieldName}\", %s{valueExpr})"
        | "bool" ->
            $"%s{indent}writer.WriteBoolean(\"%s{fieldName}\", %s{valueExpr})"
        | "byte[]" ->
            $"%s{indent}writer.WriteBase64String(\"%s{fieldName}\", %s{valueExpr})"
        | "datetime" | "system.datetime" ->
            $"%s{indent}writer.WriteString(\"%s{fieldName}\", %s{valueExpr}.ToString(\"O\"))"
        | "guid" | "system.guid" ->
            $"%s{indent}writer.WriteString(\"%s{fieldName}\", %s{valueExpr}.ToString())"
        | _ ->
            $"%s{indent}writer.WritePropertyName(\"%s{fieldName}\")\n%s{indent}JsonSerializer.Serialize(writer, %s{valueExpr}, options)"

    let writerCall (fieldName: string) (rawFieldName: string) (fsharpType: string) : string =
        let normalizedType = fsharpType.Trim().ToLowerInvariant()
        let indent = "                    "

        // Check for option types: "T option" (postfix) or "option<T>" (prefix)
        let isOption, innerType =
            if normalizedType.EndsWith(" option") then
                true, fsharpType.Trim().Substring(0, fsharpType.Trim().Length - " option".Length).Trim()
            elif normalizedType.StartsWith("option<") && normalizedType.EndsWith(">") then
                true, fsharpType.Trim().Substring("option<".Length, fsharpType.Trim().Length - "option<".Length - 1).Trim()
            else
                false, fsharpType.Trim()

        if isOption then
            let innerIndent = indent + "    "
            let innerCall = writerCallForType fieldName (innerType.ToLowerInvariant()) "v" innerIndent
            $"%s{indent}match value.%s{rawFieldName} with\n%s{indent}| Some v ->\n%s{innerCall}\n%s{indent}| None ->\n%s{innerIndent}writer.WriteNull(\"%s{fieldName}\")"
        else
            writerCallForType fieldName normalizedType $"value.%s{rawFieldName}" indent

    let lowerFirst (s: string) =
        if System.String.IsNullOrEmpty(s) then s
        else string (System.Char.ToLowerInvariant(s.[0])) + s.Substring(1)

    let upperFirst (s: string) =
        if System.String.IsNullOrEmpty(s) then s
        else string (System.Char.ToUpperInvariant(s.[0])) + s.Substring(1)

    let sanitize (name: string) =
        name
            .Replace(".", "_")
            .Replace("+", "_")
            .Replace("`", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace("[", "_")
            .Replace("]", "_")

    let fullyQualifiedName (info: SerdeTypeInfo) : string =
        let parts =
            [ yield! info.Raw.Namespace |> Option.toList
              yield! info.Raw.EnclosingModules
              yield info.Raw.TypeName ]
        String.concat "." parts

    /// Pascal name that accounts for instantiated generics (which have GenericArguments
    /// but Kind = Union/Record after substitution, so typeInfoToPascalName misses them).
    let rec private pascalNameForArg (ti: Types.TypeInfo) : string =
        if not ti.GenericArguments.IsEmpty then
            let argPart = ti.GenericArguments |> List.map pascalNameForArg |> String.concat ""
            sanitize (upperFirst ti.TypeName) + argPart
        else
            sanitize (Types.typeInfoToPascalName ti)

    /// The name used for module/converter/function names. For constructed generics,
    /// uses underscore-separated type arguments (e.g., Wrapper_Person).
    let emittedName (info: SerdeTypeInfo) : string =
        match info.GenericContext with
        | Some ctx ->
            let argNames = ctx.GenericArguments |> List.map pascalNameForArg |> String.concat ""
            sanitize $"%s{info.Raw.TypeName}_%s{argNames}"
        | None -> sanitize info.Raw.TypeName

    /// The fully-qualified F# type expression for typeof<> / JsonTypeInfo<>.
    /// For constructed generics, produces e.g. MyApp.Wrapper<MyApp.Person>.
    let emittedFqn (info: SerdeTypeInfo) : string =
        match info.GenericContext with
        | Some ctx ->
            let baseParts =
                [ yield! info.Raw.Namespace |> Option.toList
                  yield! info.Raw.EnclosingModules
                  yield info.Raw.TypeName ]
            let baseName = String.concat "." baseParts
            let argNames = ctx.GenericArguments |> List.map Types.typeInfoToFqFSharpType |> String.concat ", "
            $"%s{baseName}<%s{argNames}>"
        | None -> fullyQualifiedName info

    let emitRecord (info: SerdeTypeInfo) : string =
        let sb = StringBuilder()
        let append (s: string) = sb.AppendLine(s) |> ignore

        let fields = info.Fields |> Option.defaultValue []
        let name = emittedName info

        append "// <auto-generated />"
        append $"module rec Serde.Generated.Stj.%s{name}"
        append ""
        append "open System"
        append "open System.Text.Json"
        append "open System.Text.Json.Serialization.Metadata"
        append ""
        append "[<AutoOpen>]"
        append $"module internal %s{name}SerdeStjTypeInfo ="
        append ""

        let fnName = lowerFirst name + "StjTypeInfo"

        let fqn = emittedFqn info
        append $"    let %s{fnName} (options: JsonSerializerOptions) : JsonTypeInfo<%s{fqn}> ="

        append "        let info ="
        append $"            JsonMetadataServices.CreateObjectInfo<%s{fqn}>("
        append "                options,"
        append $"                JsonObjectInfoValues<%s{fqn}>("

        // ObjectWithParameterizedConstructorCreator: constructs F# record from args array
        append "                    ObjectWithParameterizedConstructorCreator = (fun (args: obj[]) ->"
        let fieldAssignments =
            fields
            |> List.mapi (fun i field ->
                let fsharpType = Types.typeInfoToFqFSharpType field.Type
                $"%s{field.RawName} = args.[%d{i}] :?> %s{fsharpType}")
            |> String.concat "; "
        append $"                        {{ %s{fieldAssignments} }} : %s{fqn}),"

        // ConstructorParameterMetadataInitializer: describes each constructor parameter
        append "                    ConstructorParameterMetadataInitializer = (fun _ ->"
        append "                        [|"
        for i, field in fields |> List.mapi (fun i x -> i, x) do
            let fsharpType = Types.typeInfoToFqFSharpType field.Type
            append $"                            JsonParameterInfoValues(Name = \"%s{lowerFirst field.Name}\", ParameterType = typeof<%s{fsharpType}>, Position = %d{i})"
        append "                        |]"
        append "                    ),"

        append "                    PropertyMetadataInitializer = (fun _ ->"
        append "                        [|"

        for field in fields do
            let fsharpType = Types.typeInfoToFqFSharpType field.Type
            append $"                            JsonMetadataServices.CreatePropertyInfo<%s{fsharpType}>("
            append "                                options,"
            append $"                                JsonPropertyInfoValues<%s{fsharpType}>("
            append "                                    IsProperty = true,"
            append "                                    IsPublic = true,"
            append $"                                    DeclaringType = typeof<%s{fqn}>,"
            append $"                                    PropertyName = \"%s{field.Name}\","
            append $"                                    Getter = (fun (obj: obj) -> (obj :?> %s{fqn}).%s{field.RawName})"
            append "                                )"
            append "                            )"

        append "                        |]"
        append "                    ),"
        append "                    SerializeHandler = (fun writer value ->"
        append "                    writer.WriteStartObject()"

        for field in fields do
            let fsharpType = Types.typeInfoToFqFSharpType field.Type
            let call = writerCall field.Name field.RawName fsharpType
            append call

        append "                    writer.WriteEndObject()"
        append "                )"
        append "            )"
        append "        )"

        append "        info"

        sb.ToString()

    let emitOption (info: SerdeTypeInfo) : string =
        let inner =
            match info.Raw.Kind with
            | Types.Option inner -> inner
            | _ -> failwith "emitOption called with non-Option kind"

        let pascalName = Types.typeInfoToPascalName info.Raw
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let innerFqType = Types.typeInfoToFqFSharpType inner
        let converterName = pascalName + "StjConverter"
        let fnName = lowerFirst pascalName + "StjTypeInfo"

        let sb = StringBuilder()
        let append (s: string) = sb.AppendLine(s) |> ignore

        append "// <auto-generated />"
        append $"module rec Serde.Generated.Stj.%s{pascalName}"
        append ""
        append "open System.Text.Json"
        append "open System.Text.Json.Serialization"
        append "open System.Text.Json.Serialization.Metadata"
        append ""
        append "[<AutoOpen>]"
        append $"module internal %s{pascalName}SerdeStjTypeInfo ="
        append ""
        append $"    type internal %s{converterName}() ="
        append $"        inherit JsonConverter<%s{fqType}>()"
        append "        override _.Read(reader, _typeToConvert, options) ="
        append "            if reader.TokenType = JsonTokenType.Null then None"
        append $"            else Some(JsonSerializer.Deserialize<%s{innerFqType}>(&reader, options))"
        append "        override _.Write(writer, value, options) ="
        append "            match value with"
        append "            | None -> writer.WriteNullValue()"
        append "            | Some v -> JsonSerializer.Serialize(writer, v, options)"
        append ""
        append $"    let %s{fnName} (options: JsonSerializerOptions) : JsonTypeInfo<%s{fqType}> ="
        append $"        JsonMetadataServices.CreateValueInfo<%s{fqType}>(options, %s{converterName}())"

        sb.ToString()

    let emitTuple (info: SerdeTypeInfo) : string =
        let elements =
            match info.Raw.Kind with
            | Types.Tuple elems -> elems
            | _ -> failwith "emitTuple called with non-Tuple kind"

        let pascalName = Types.typeInfoToPascalName info.Raw
        let fqType = Types.typeInfoToFqFSharpType info.Raw
        let converterName = pascalName + "StjConverter"
        let fnName = lowerFirst pascalName + "StjTypeInfo"

        let sb = StringBuilder()
        let append (s: string) = sb.AppendLine(s) |> ignore

        append "// <auto-generated />"
        append $"module rec Serde.Generated.Stj.%s{pascalName}"
        append ""
        append "open System.Text.Json"
        append "open System.Text.Json.Serialization"
        append "open System.Text.Json.Serialization.Metadata"
        append ""
        append "[<AutoOpen>]"
        append $"module internal %s{pascalName}SerdeStjTypeInfo ="
        append ""
        append $"    type internal %s{converterName}() ="
        append $"        inherit JsonConverter<%s{fqType}>()"
        append "        override _.Read(reader, _typeToConvert, options) ="
        append "            if reader.TokenType <> JsonTokenType.StartArray then"
        append "                raise (JsonException(\"Expected StartArray for tuple\"))"
        for i, elem in elements |> List.mapi (fun i x -> i, x) do
            let elemFqType = Types.typeInfoToFqFSharpType elem.Type
            append "            reader.Read() |> ignore"
            append $"            let e%d{i} = JsonSerializer.Deserialize<%s{elemFqType}>(&reader, options)"
        append "            reader.Read() |> ignore"
        let tupleExpr = elements |> List.mapi (fun i _ -> $"e%d{i}") |> String.concat ", "
        append $"            (%s{tupleExpr})"
        append "        override _.Write(writer, value, options) ="
        append "            writer.WriteStartArray()"
        let destructure = elements |> List.mapi (fun i _ -> $"e%d{i}") |> String.concat ", "
        append $"            let (%s{destructure}) = value"
        for i, _elem in elements |> List.mapi (fun i x -> i, x) do
            append $"            JsonSerializer.Serialize(writer, e%d{i}, options)"
        append "            writer.WriteEndArray()"
        append ""
        append $"    let %s{fnName} (options: JsonSerializerOptions) : JsonTypeInfo<%s{fqType}> ="
        append $"        JsonMetadataServices.CreateValueInfo<%s{fqType}>(options, %s{converterName}())"

        sb.ToString()

    type private CaseShape = Nullary | SingleField | TupleFields | RecordFields

    let private classifyCaseShape (case: SerdeUnionCaseInfo) =
        match case.Fields with
        | [] -> Nullary
        | [_] -> SingleField
        | fields ->
            if fields |> List.forall (fun f -> f.RawName = "Item") then TupleFields
            else RecordFields

    type private UnionKind = WrapperUnion | MultiCaseUnion

    let private classifyUnionKind (cases: SerdeUnionCaseInfo list) =
        match cases with
        | [single] when single.Fields.Length = 1 -> WrapperUnion
        | _ -> MultiCaseUnion

    let emitUnion (info: SerdeTypeInfo) : string =
        let cases = info.UnionCases |> Option.defaultValue []
        let fqn = emittedFqn info
        // For generic unions, qualify cases with just namespace/module (not the type)
        // because F# can't use generic args in pattern paths.
        let caseFqn =
            match info.GenericContext with
            | Some _ ->
                let parts =
                    [ yield! info.Raw.Namespace |> Option.toList
                      yield! info.Raw.EnclosingModules ]
                if parts.IsEmpty then info.Raw.TypeName
                else String.concat "." parts
            | None -> fullyQualifiedName info
        let pascalName = emittedName info
        let converterName = pascalName + "StjConverter"
        let fnName = lowerFirst pascalName + "StjTypeInfo"

        let activeCases = cases |> List.filter (fun c -> not c.Attributes.Skip)
        let hasSkippedCases = cases |> List.exists (fun c -> c.Attributes.Skip)
        let kind = classifyUnionKind cases

        let sb = StringBuilder()
        let append (s: string) = sb.AppendLine(s) |> ignore

        append "// <auto-generated />"
        append $"module rec Serde.Generated.Stj.%s{pascalName}"
        append ""
        append "open System.Text.Json"
        append "open System.Text.Json.Serialization"
        append "open System.Text.Json.Serialization.Metadata"
        append ""
        append "[<AutoOpen>]"
        append $"module internal %s{pascalName}SerdeStjTypeInfo ="
        append ""
        append $"    type internal %s{converterName}() ="
        append $"        inherit JsonConverter<%s{fqn}>()"

        match kind with
        | WrapperUnion ->
            let case = activeCases.[0]

            // Read override
            append "        override _.Read(reader, _typeToConvert, options) ="
            append "            if reader.TokenType <> JsonTokenType.StartObject then"
            append "                raise (JsonException(\"Expected StartObject for union\"))"
            append "            reader.Read() |> ignore"
            append "            let caseName = reader.GetString()"
            append "            reader.Read() |> ignore"
            append $"            if caseName = \"%s{case.CaseName}\" then"
            let field = case.Fields.[0]
            let fsharpType = Types.typeInfoToFqFSharpType field.Type
            append $"                let v = JsonSerializer.Deserialize<%s{fsharpType}>(&reader, options)"
            append "                reader.Read() |> ignore"
            append $"                %s{caseFqn}.%s{case.RawCaseName}(v)"
            append $"            else raise (JsonException($\"Unknown union case: %%s{{caseName}}\"))"

            // Write override
            append "        override _.Write(writer, value, options) ="
            append "            writer.WriteStartObject()"
            append "            match value with"
            append $"            | %s{caseFqn}.%s{case.RawCaseName}(v) ->"
            append $"                writer.WritePropertyName(\"%s{case.CaseName}\")"
            append "                JsonSerializer.Serialize(writer, v, options)"
            append "            writer.WriteEndObject()"

        | MultiCaseUnion ->
            // Read override
            append "        override _.Read(reader, _typeToConvert, options) ="
            append "            if reader.TokenType <> JsonTokenType.StartObject then"
            append "                raise (JsonException(\"Expected StartObject for union\"))"
            append "            reader.Read() |> ignore"
            append "            if reader.GetString() <> \"Case\" then"
            append "                raise (JsonException(\"Expected 'Case' property\"))"
            append "            reader.Read() |> ignore"
            append "            let caseName = reader.GetString()"
            append "            reader.Read() |> ignore"
            append "            if reader.GetString() <> \"Fields\" then"
            append "                raise (JsonException(\"Expected 'Fields' property\"))"
            append "            reader.Read() |> ignore"
            append "            if reader.TokenType <> JsonTokenType.StartArray then"
            append "                raise (JsonException(\"Expected StartArray for Fields\"))"
            append $"            let mutable result = Unchecked.defaultof<%s{fqn}>"

            for i, case in activeCases |> List.mapi (fun i x -> i, x) do
                let keyword = if i = 0 then "if" else "elif"
                let shape = classifyCaseShape case
                append $"            %s{keyword} caseName = \"%s{case.CaseName}\" then"
                match shape with
                | Nullary ->
                    append "                reader.Read() |> ignore"
                    append $"                result <- %s{caseFqn}.%s{case.RawCaseName}"
                | SingleField ->
                    let field = case.Fields.[0]
                    let fsharpType = Types.typeInfoToFqFSharpType field.Type
                    append "                reader.Read() |> ignore"
                    append $"                let v = JsonSerializer.Deserialize<%s{fsharpType}>(&reader, options)"
                    append "                reader.Read() |> ignore"
                    append $"                result <- %s{caseFqn}.%s{case.RawCaseName}(v)"
                | TupleFields ->
                    for j, field in case.Fields |> List.mapi (fun j x -> j, x) do
                        let fsharpType = Types.typeInfoToFqFSharpType field.Type
                        append "                reader.Read() |> ignore"
                        append $"                let e%d{j} = JsonSerializer.Deserialize<%s{fsharpType}>(&reader, options)"
                    append "                reader.Read() |> ignore"
                    let args = case.Fields |> List.mapi (fun j _ -> $"e%d{j}") |> String.concat ", "
                    append $"                result <- %s{caseFqn}.%s{case.RawCaseName}(%s{args})"
                | RecordFields ->
                    for j, field in case.Fields |> List.mapi (fun j x -> j, x) do
                        let fsharpType = Types.typeInfoToFqFSharpType field.Type
                        append "                reader.Read() |> ignore"
                        append $"                let e%d{j} = JsonSerializer.Deserialize<%s{fsharpType}>(&reader, options)"
                    append "                reader.Read() |> ignore"
                    let args = case.Fields |> List.mapi (fun j _ -> $"e%d{j}") |> String.concat ", "
                    append $"                result <- %s{caseFqn}.%s{case.RawCaseName}(%s{args})"

            append $"            else raise (JsonException($\"Unknown union case: %%s{{caseName}}\"))"
            append "            reader.Read() |> ignore"
            append "            result"

            // Write override
            append "        override _.Write(writer, value, options) ="
            append "            writer.WriteStartObject()"
            append "            match value with"

            for case in activeCases do
                let shape = classifyCaseShape case
                match shape with
                | Nullary ->
                    append $"            | %s{caseFqn}.%s{case.RawCaseName} ->"
                    append $"                writer.WriteString(\"Case\", \"%s{case.CaseName}\")"
                    append "                writer.WritePropertyName(\"Fields\")"
                    append "                writer.WriteStartArray()"
                    append "                writer.WriteEndArray()"
                | SingleField ->
                    append $"            | %s{caseFqn}.%s{case.RawCaseName}(v) ->"
                    append $"                writer.WriteString(\"Case\", \"%s{case.CaseName}\")"
                    append "                writer.WritePropertyName(\"Fields\")"
                    append "                writer.WriteStartArray()"
                    append "                JsonSerializer.Serialize(writer, v, options)"
                    append "                writer.WriteEndArray()"
                | TupleFields ->
                    let args = case.Fields |> List.mapi (fun j _ -> $"e%d{j}") |> String.concat ", "
                    append $"            | %s{caseFqn}.%s{case.RawCaseName}(%s{args}) ->"
                    append $"                writer.WriteString(\"Case\", \"%s{case.CaseName}\")"
                    append "                writer.WritePropertyName(\"Fields\")"
                    append "                writer.WriteStartArray()"
                    for j in 0 .. case.Fields.Length - 1 do
                        append $"                JsonSerializer.Serialize(writer, e%d{j}, options)"
                    append "                writer.WriteEndArray()"
                | RecordFields ->
                    let args = case.Fields |> List.mapi (fun j _ -> $"e%d{j}") |> String.concat ", "
                    append $"            | %s{caseFqn}.%s{case.RawCaseName}(%s{args}) ->"
                    append $"                writer.WriteString(\"Case\", \"%s{case.CaseName}\")"
                    append "                writer.WritePropertyName(\"Fields\")"
                    append "                writer.WriteStartArray()"
                    for j in 0 .. case.Fields.Length - 1 do
                        append $"                JsonSerializer.Serialize(writer, e%d{j}, options)"
                    append "                writer.WriteEndArray()"

            if hasSkippedCases then
                append "            | _ -> raise (JsonException(\"Unknown or skipped union case\"))"

            append "            writer.WriteEndObject()"

        append ""
        append $"    let %s{fnName} (options: JsonSerializerOptions) : JsonTypeInfo<%s{fqn}> ="
        append $"        JsonMetadataServices.CreateValueInfo<%s{fqn}>(options, %s{converterName}())"

        sb.ToString()

    let emitEnum (info: SerdeTypeInfo) : string =
        let cases = info.EnumCases |> Option.defaultValue []
        let fqn = emittedFqn info
        let pascalName = emittedName info
        let converterName = pascalName + "StjConverter"
        let fnName = lowerFirst pascalName + "StjTypeInfo"

        let sb = StringBuilder()
        let append (s: string) = sb.AppendLine(s) |> ignore

        append "// <auto-generated />"
        append $"module rec Serde.Generated.Stj.%s{pascalName}"
        append ""
        append "open System.Text.Json"
        append "open System.Text.Json.Serialization"
        append "open System.Text.Json.Serialization.Metadata"
        append ""
        append "[<AutoOpen>]"
        append $"module internal %s{pascalName}SerdeStjTypeInfo ="
        append ""
        append $"    type internal %s{converterName}() ="
        append $"        inherit JsonConverter<%s{fqn}>()"
        append "        override _.Read(reader, _typeToConvert, _options) ="
        append "            let s = reader.GetString()"

        let activeCases = cases |> List.filter (fun c -> not c.Attributes.Skip)
        for i, c in activeCases |> List.mapi (fun i x -> i, x) do
            let keyword = if i = 0 then "if" else "elif"
            append $"            %s{keyword} s = \"%s{c.CaseName}\" then %s{fqn}.%s{c.RawCaseName}"

        append $"            else raise (JsonException($\"Unknown enum value: %%s{{s}}\"))"

        append "        override _.Write(writer, value, _options) ="

        for i, c in activeCases |> List.mapi (fun i x -> i, x) do
            let keyword = if i = 0 then "if" else "elif"
            append $"            %s{keyword} value = %s{fqn}.%s{c.RawCaseName} then writer.WriteStringValue(\"%s{c.CaseName}\")"

        append $"            else raise (JsonException($\"Unknown enum value: %%A{{value}}\"))"

        append ""
        append $"    let %s{fnName} (options: JsonSerializerOptions) : JsonTypeInfo<%s{fqn}> ="
        append $"        JsonMetadataServices.CreateValueInfo<%s{fqn}>(options, %s{converterName}())"

        sb.ToString()

    let emit (info: SerdeTypeInfo) : string =
        // STJ emitter does not support codec overrides — skip CodecType
        match info.Raw.Kind with
        | Types.Option _ -> emitOption info
        | Types.Tuple _ -> emitTuple info
        | Types.Enum _ -> emitEnum info
        | Types.Union _ -> emitUnion info
        | _ -> emitRecord info

    let private resolverModuleName (info: SerdeTypeInfo) : string =
        match info.Raw.Kind with
        | Types.Option _ | Types.Tuple _ -> Types.typeInfoToPascalName info.Raw
        | _ -> emittedName info

    let private resolverFqn (info: SerdeTypeInfo) : string =
        match info.Raw.Kind with
        | Types.Option _ | Types.Tuple _ -> Types.typeInfoToFqFSharpType info.Raw
        | _ -> emittedFqn info

    let private resolverFnName (info: SerdeTypeInfo) : string =
        match info.Raw.Kind with
        | Types.Option _ | Types.Tuple _ -> lowerFirst (Types.typeInfoToPascalName info.Raw) + "StjTypeInfo"
        | _ -> lowerFirst (emittedName info) + "StjTypeInfo"

    let emitResolver (types: SerdeTypeInfo list) : string option =
        match types with
        | [] -> None
        | _ ->
            let sb = StringBuilder()
            let append (s: string) = sb.AppendLine(s) |> ignore

            append "// <auto-generated />"
            append "namespace Serde.FS.SystemTextJson"
            append ""
            append "open System"
            append "open System.Text.Json"
            append "open System.Text.Json.Serialization.Metadata"

            for info in types do
                append $"open Serde.Generated.Stj.%s{resolverModuleName info}"

            append ""
            append "type GeneratedSerdeStjResolver() ="
            append "    interface IJsonTypeInfoResolver with"
            append "        member _.GetTypeInfo(t: Type, options: JsonSerializerOptions) : JsonTypeInfo ="

            for i, info in types |> List.mapi (fun i x -> i, x) do
                let fqn = resolverFqn info
                let fnName = resolverFnName info
                let keyword = if i = 0 then "if" else "elif"
                append $"            %s{keyword} t = typeof<%s{fqn}> then %s{fnName} options :> JsonTypeInfo"

            append "            else null"

            Some (sb.ToString())

type StjCodeEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) = StjCodeEmitterImpl.emit info
    interface ISerdeResolverEmitter with
        member _.EmitResolver(types) = StjCodeEmitterImpl.emitResolver types
        member _.ResolverHintName = "~SerdeStjResolver.g.fs"
        member _.EmitRegistrationFiles() =
            let resolverModuleCode =
                "// <auto-generated />\n" +
                "namespace Serde.FS.SystemTextJson\n" +
                "\n" +
                "open System.Text.Json.Serialization.Metadata\n" +
                "\n" +
                "module Resolver =\n" +
                "\n" +
                "    /// Shared STJ resolver instance backed by GeneratedSerdeStjResolver.\n" +
                "    let resolver : IJsonTypeInfoResolver =\n" +
                "        GeneratedSerdeStjResolver() :> IJsonTypeInfoResolver\n" +
                "\n" +
                "    /// Register this resolver with the Serde runtime bootstrap hook.\n" +
                "    let registerAll () =\n" +
                "        Serde.ResolverBootstrap.registerAll <-\n" +
                "            Some (fun () -> box resolver)\n"
            let bootstrapCode =
                "// <auto-generated />\n" +
                "namespace Djinn.Generated\n" +
                "\n" +
                "module Bootstrap =\n" +
                "\n" +
                "    /// Called by FSharp.SourceDjinn's generated entry point via convention.\n" +
                "    let init () =\n" +
                "        Serde.FS.SystemTextJson.Resolver.registerAll ()\n"
            [
                ("~SerdeStjResolverModule.g.fs", resolverModuleCode)
                ("~SerdeStjBootstrap.g.fs", bootstrapCode)
            ]
        member _.EmitEntryPoint = true
        member _.EmitPerTypeFiles = true
