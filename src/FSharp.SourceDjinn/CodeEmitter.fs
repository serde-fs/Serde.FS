namespace FSharp.SourceDjinn

open Serde.FS

module CodeEmitter =
    let emit (emitter: ISerdeCodeEmitter) (info: SerdeTypeInfo) : string =
        emitter.Emit(info)
