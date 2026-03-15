namespace Serde.FS.Json.Codec

open System.Text

/// Pure Serde-native JSON writer. Produces minimal JSON from a JsonValue AST
/// with no dependency on System.Text.Json.
module internal SerdeJsonWriter =

    let private escapeString (sb: StringBuilder) (s: string) =
        sb.Append('"') |> ignore
        for c in s do
            match c with
            | '"'  -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\u000C' -> sb.Append("\\f") |> ignore
            | c when System.Char.IsControl(c) ->
                sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append(c) |> ignore
        sb.Append('"') |> ignore

    let rec private writeValue (sb: StringBuilder) (value: JsonValue) =
        match value with
        | JsonValue.Null -> sb.Append("null") |> ignore
        | JsonValue.Bool b -> sb.Append(if b then "true" else "false") |> ignore
        | JsonValue.Number n -> sb.Append(n.ToString("G")) |> ignore
        | JsonValue.String s -> escapeString sb s
        | JsonValue.Array items ->
            sb.Append('[') |> ignore
            match items with
            | [] -> ()
            | first :: rest ->
                writeValue sb first
                for item in rest do
                    sb.Append(',') |> ignore
                    writeValue sb item
            sb.Append(']') |> ignore
        | JsonValue.Object fields ->
            sb.Append('{') |> ignore
            match fields with
            | [] -> ()
            | (name, value) :: rest ->
                escapeString sb name
                sb.Append(':') |> ignore
                writeValue sb value
                for (name, value) in rest do
                    sb.Append(',') |> ignore
                    escapeString sb name
                    sb.Append(':') |> ignore
                    writeValue sb value
            sb.Append('}') |> ignore

    /// Writes a JsonValue to a JSON string.
    let writeToString (value: JsonValue) : string =
        let sb = StringBuilder()
        writeValue sb value
        sb.ToString()

    /// Writes a JsonValue to a UTF-8 byte array.
    let writeToUtf8 (value: JsonValue) : byte[] =
        writeToString value |> Encoding.UTF8.GetBytes
