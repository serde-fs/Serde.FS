namespace Serde.FS.Json.Codec

open System
open System.Text
open Serde.FS

/// Pure Serde-native JSON reader/parser. Parses UTF-8 bytes or strings into
/// a JsonValue AST with no dependency on System.Text.Json.
module internal SerdeJsonReader =

    [<Struct>]
    type private Parser =
        { mutable Pos: int
          Input: string }

    let private peek (p: Parser byref) =
        if p.Pos < p.Input.Length then p.Input.[p.Pos]
        else raise (SerdeJsonParseException("Unexpected end of input", p.Pos))

    let private advance (p: Parser byref) =
        p.Pos <- p.Pos + 1

    let private skipWhitespace (p: Parser byref) =
        while p.Pos < p.Input.Length && Char.IsWhiteSpace(p.Input.[p.Pos]) do
            p.Pos <- p.Pos + 1

    let private expect (p: Parser byref) (expected: string) =
        for i in 0 .. expected.Length - 1 do
            if p.Pos >= p.Input.Length then
                raise (SerdeJsonParseException($"Unexpected end of input, expected '{expected}'", p.Pos))
            if p.Input.[p.Pos] <> expected.[i] then
                raise (SerdeJsonParseException($"Expected '{expected.[i]}' but got '{p.Input.[p.Pos]}'", p.Pos))
            p.Pos <- p.Pos + 1

    let private parseString (p: Parser byref) : string =
        let start = p.Pos
        if peek &p <> '"' then
            raise (SerdeJsonParseException($"Expected '\"' but got '{peek &p}'", p.Pos))
        advance &p // skip opening quote
        let sb = StringBuilder()
        let mutable cont = true
        while cont do
            if p.Pos >= p.Input.Length then
                raise (SerdeJsonParseException("Unterminated string", start))
            let c = p.Input.[p.Pos]
            match c with
            | '"' ->
                advance &p // skip closing quote
                cont <- false
            | '\\' ->
                advance &p
                if p.Pos >= p.Input.Length then
                    raise (SerdeJsonParseException("Unterminated escape sequence", p.Pos))
                match p.Input.[p.Pos] with
                | '"'  -> sb.Append('"')  |> ignore; advance &p
                | '\\' -> sb.Append('\\') |> ignore; advance &p
                | '/'  -> sb.Append('/')  |> ignore; advance &p
                | 'n'  -> sb.Append('\n') |> ignore; advance &p
                | 'r'  -> sb.Append('\r') |> ignore; advance &p
                | 't'  -> sb.Append('\t') |> ignore; advance &p
                | 'b'  -> sb.Append('\b') |> ignore; advance &p
                | 'f'  -> sb.Append('\u000C') |> ignore; advance &p
                | 'u'  ->
                    advance &p
                    if p.Pos + 4 > p.Input.Length then
                        raise (SerdeJsonParseException("Incomplete \\uXXXX escape", p.Pos))
                    let hex = p.Input.Substring(p.Pos, 4)
                    match Int32.TryParse(hex, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture) with
                    | true, code ->
                        sb.Append(char code) |> ignore
                        p.Pos <- p.Pos + 4
                    | _ ->
                        raise (SerdeJsonParseException($"Invalid unicode escape '\\u{hex}'", p.Pos))
                | esc ->
                    raise (SerdeJsonParseException($"Invalid escape character '\\{esc}'", p.Pos))
            | _ ->
                sb.Append(c) |> ignore
                advance &p
        sb.ToString()

    let private parseNumber (p: Parser byref) : decimal =
        let start = p.Pos
        // Consume characters that can be part of a JSON number
        while p.Pos < p.Input.Length &&
              (let c = p.Input.[p.Pos]
               c = '-' || c = '+' || c = '.' || c = 'e' || c = 'E' || (c >= '0' && c <= '9')) do
            p.Pos <- p.Pos + 1
        let numStr = p.Input.Substring(start, p.Pos - start)
        match Decimal.TryParse(numStr, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, n -> n
        | _ ->
            raise (SerdeJsonParseException($"Invalid number '{numStr}'", start))

    let rec private parseValue (p: Parser byref) : JsonValue =
        skipWhitespace &p
        if p.Pos >= p.Input.Length then
            raise (SerdeJsonParseException("Unexpected end of input", p.Pos))
        match p.Input.[p.Pos] with
        | 'n' ->
            expect &p "null"
            JsonValue.Null
        | 't' ->
            expect &p "true"
            JsonValue.Bool true
        | 'f' ->
            expect &p "false"
            JsonValue.Bool false
        | '"' ->
            JsonValue.String(parseString &p)
        | '[' ->
            advance &p
            skipWhitespace &p
            if p.Pos < p.Input.Length && p.Input.[p.Pos] = ']' then
                advance &p
                JsonValue.Array []
            else
                let mutable items = []
                let mutable cont = true
                while cont do
                    items <- items @ [ parseValue &p ]
                    skipWhitespace &p
                    if p.Pos >= p.Input.Length then
                        raise (SerdeJsonParseException("Unterminated array", p.Pos))
                    match p.Input.[p.Pos] with
                    | ',' ->
                        advance &p
                    | ']' ->
                        advance &p
                        cont <- false
                    | c ->
                        raise (SerdeJsonParseException($"Expected ',' or ']' but got '{c}'", p.Pos))
                JsonValue.Array items
        | '{' ->
            advance &p
            skipWhitespace &p
            if p.Pos < p.Input.Length && p.Input.[p.Pos] = '}' then
                advance &p
                JsonValue.Object []
            else
                let mutable fields = []
                let mutable cont = true
                while cont do
                    skipWhitespace &p
                    let name = parseString &p
                    skipWhitespace &p
                    if p.Pos >= p.Input.Length || p.Input.[p.Pos] <> ':' then
                        raise (SerdeJsonParseException($"Expected ':' after property name", p.Pos))
                    advance &p
                    let value = parseValue &p
                    fields <- fields @ [ (name, value) ]
                    skipWhitespace &p
                    if p.Pos >= p.Input.Length then
                        raise (SerdeJsonParseException("Unterminated object", p.Pos))
                    match p.Input.[p.Pos] with
                    | ',' ->
                        advance &p
                    | '}' ->
                        advance &p
                        cont <- false
                    | c ->
                        raise (SerdeJsonParseException($"Expected ',' or '}}' but got '{c}'", p.Pos))
                JsonValue.Object fields
        | '-' | '0' | '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9' ->
            JsonValue.Number(parseNumber &p)
        | c ->
            raise (SerdeJsonParseException($"Unexpected character '{c}'", p.Pos))

    /// Parses a JSON string into a JsonValue AST.
    let readFromString (json: string) : JsonValue =
        let mutable p = { Pos = 0; Input = json }
        let result = parseValue &p
        skipWhitespace &p
        if p.Pos < p.Input.Length then
            raise (SerdeJsonParseException($"Unexpected trailing content at position {p.Pos}", p.Pos))
        result

    /// Parses a UTF-8 byte array into a JsonValue AST.
    let readFromUtf8 (bytes: byte[]) : JsonValue =
        let json = Encoding.UTF8.GetString(bytes)
        readFromString json
