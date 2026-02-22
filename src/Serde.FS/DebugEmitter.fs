namespace Serde.FS

type DebugEmitter() =
    interface ISerdeCodeEmitter with
        member _.Emit(info) = sprintf "// DEBUG EMIT: %s" info.TypeName

module SerdeDebug =
    let useAsDefault () =
        SerdeCodegenRegistry.setDefaultEmitter (DebugEmitter())
