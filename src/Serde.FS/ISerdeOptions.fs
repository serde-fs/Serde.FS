namespace Serde.FS

/// Options interface for backend-specific configuration.
type ISerdeOptions =
    /// When true, serialization/deserialization of types without [<Serde>] attributes throws.
    abstract Strict : bool with get, set
    /// When true, enables debug logging for Serde operations.
    abstract Debug : bool with get, set
