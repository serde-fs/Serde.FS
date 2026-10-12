namespace Serde.FS.Json.Codec

/// Deterministic JSON AST for codec-based serialization.
type JsonValue =
    | Null
    | Bool of bool
    | Number of decimal
    /// A JSON number carried as a double. `decimal` cannot represent every
    /// `float` (15 significant digits on conversion, nothing above ~7.9e28 or
    /// below 1e-28), so `float` values are encoded with this case. The reader
    /// produces it for number tokens written in exponent notation, or too
    /// large for a `decimal`; every other number token is a `Number`.
    | Float of float
    | String of string
    | Array of JsonValue list
    | Object of (string * JsonValue) list

module JsonValue =
    /// Returns a compact string representation of a JsonValue for debugging.
    let rec toString (value: JsonValue) =
        match value with
        | Null -> "null"
        | Bool b -> if b then "true" else "false"
        | Number n -> n.ToString(System.Globalization.CultureInfo.InvariantCulture)
        | Float f -> f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        | String s -> $"\"{s}\""
        | Array items ->
            let inner = items |> List.map toString |> String.concat ", "
            $"[{inner}]"
        | Object fields ->
            let inner =
                fields
                |> List.map (fun (k, v) -> $"\"{k}\": {toString v}")
                |> String.concat ", "
            $"{{{inner}}}"
