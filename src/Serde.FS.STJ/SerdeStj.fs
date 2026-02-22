module Serde.FS.STJ.SerdeStj

open Serde.FS

/// Sets System.Text.Json as the default backend for Serde.FS.
/// Call once at application startup.
 let useAsDefault () =
    match Serde.DefaultBackend with
    | Some (:? StjBackend) -> ()
    | _ -> Serde.DefaultBackend <- Some (StjBackend() :> ISerdeBackend)
    SerdeCodegenRegistry.setDefaultEmitter (StjCodeEmitter() :> ISerdeCodeEmitter)
