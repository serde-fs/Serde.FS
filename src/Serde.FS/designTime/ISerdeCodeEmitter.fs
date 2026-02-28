namespace Serde.FS

type ISerdeCodeEmitter =
    abstract member Emit : SerdeTypeInfo -> string

type ISerdeResolverEmitter =
    abstract member EmitResolver : SerdeTypeInfo list -> string option

module SerdeCodegenRegistry =
    let mutable private defaultEmitter : ISerdeCodeEmitter option = None
    let setDefaultEmitter emitter = defaultEmitter <- Some emitter
    let getDefaultEmitter () = defaultEmitter
