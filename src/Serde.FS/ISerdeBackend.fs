namespace Serde.FS

type ISerdeBackend =
    abstract Serialize : 'T * ISerdeOptions option -> string
    abstract Deserialize : string * ISerdeOptions option -> 'T
