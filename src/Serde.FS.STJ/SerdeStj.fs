module Serde.FS.STJ.SerdeStj

open Serde.FS

/// Sets System.Text.Json as the default backend for Serde.FS.
/// Call once at application startup. Enables strict mode by default.
let useAsDefault () =
    match Serde.DefaultBackend with
    | Some (:? StjBackend) -> ()
    | _ -> Serde.DefaultBackend <- Some (StjBackend() :> ISerdeBackend)
    SerdeCodegenRegistry.setDefaultEmitter (StjCodeEmitter() :> ISerdeCodeEmitter)
    Serde.Strict <- true

/// The global STJ options instance. Strict mode is enabled by default.
let options = SerdeStjDefaults.options

/// Apply a configuration function to the global STJ options.
let configure (f: SerdeStjOptions -> unit) = f options

/// Disables strict mode, allowing reflection-based serialization for types
/// without generated Serde metadata.
let allowReflectionFallback () = options.Strict <- false

/// Enables debug logging for Serde operations.
let enableDebug () = Serde.Debug <- true
