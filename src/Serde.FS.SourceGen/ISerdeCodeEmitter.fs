namespace Serde.FS

type ISerdeCodeEmitter =
    abstract member Emit : SerdeTypeInfo -> string

type ISerdeResolverEmitter =
    abstract member EmitResolver : SerdeTypeInfo list -> string option
    /// The hint name for the resolver file (e.g. "~SerdeResolver.serde.g.fs" or "~SerdeStjResolver.g.fs").
    abstract member ResolverHintName : string
    /// Additional files to emit after the resolver (e.g. registration + bootstrap). Returns (hintName, code) pairs.
    abstract member EmitRegistrationFiles : unit -> (string * string) list
    /// Whether this backend emits per-type files (true) or consolidates all codecs into the resolver (false).
    abstract member EmitPerTypeFiles : bool

module SerdeCodegenRegistry =
    let mutable private defaultEmitter : ISerdeCodeEmitter option = None
    let setDefaultEmitter emitter = defaultEmitter <- Some emitter
    let getDefaultEmitter () = defaultEmitter
