namespace Serde.FS

/// Thrown when JSON parsing fails with position and token information.
type SerdeJsonParseException(message: string, position: int, ?inner: System.Exception) =
    inherit SerdeJsonException(message, defaultArg inner null)
    /// The byte/character position in the input where the error occurred.
    member _.Position = position
