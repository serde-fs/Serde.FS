namespace Serde.FS

/// Marker interface for backend-specific options.
type ISerdeOptions = interface end

type ISerdeBackend =
    abstract Serialize : 'T * ISerdeOptions option -> string
    abstract Deserialize : string * ISerdeOptions option -> 'T

type Serde =
    static member val DefaultBackend : ISerdeBackend =
        failwith "No backend registered. Install a Serde.FS backend package."

    static member Serialize(value: 'T) =
        Serde.DefaultBackend.Serialize(value, None)

    static member Serialize(value: 'T, options: ISerdeOptions) =
        Serde.DefaultBackend.Serialize(value, Some options)

    static member Deserialize<'T>(json: string) =
        Serde.DefaultBackend.Deserialize<'T>(json, None)

    static member Deserialize<'T>(json: string, options: ISerdeOptions) =
        Serde.DefaultBackend.Deserialize<'T>(json, Some options)
