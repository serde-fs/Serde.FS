namespace Serde.FS.SourceGen

open Serde.FS

module CodeEmitter =
    let emit (emitter: ISerdeCodeEmitter) (info: SerdeTypeInfo) : string =
        emitter.Emit(info)
