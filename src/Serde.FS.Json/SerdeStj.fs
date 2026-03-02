module Serde.FS.Json.SerdeStj

open Serde.FS

/// Sets System.Text.Json as the default backend for Serde.FS.
/// Call once at application startup. Enables strict mode by default.
let private triggerBootstrap () =
    match global.Serde.ResolverBootstrap.registerAll with
    | Some _ -> ()
    | None ->
        let asm = System.Reflection.Assembly.GetEntryAssembly()
        if not (isNull asm) then
            match asm.GetType("Djinn.Generated.Bootstrap") with
            | null -> ()
            | ty ->
                match ty.GetMethod("init", System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static) with
                | null -> ()
                | m -> m.Invoke(null, [||]) |> ignore

let useAsDefault () =
    triggerBootstrap ()
    match global.Serde.ResolverBootstrap.registerAll with
    | Some f -> f()
    | None -> ()
    match Serde.DefaultBackend with
    | Some (:? StjBackend) -> ()
    | _ -> Serde.DefaultBackend <- Some (StjBackend() :> ISerdeBackend)
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
