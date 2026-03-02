module Serde.FS.Json.SerdeJson

open Serde.FS

/// Sets System.Text.Json as the default backend for Serde.FS.
/// Call once at application startup.
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
    | Some (:? JsonBackend) -> ()
    | _ -> Serde.DefaultBackend <- Some (JsonBackend() :> ISerdeBackend)

/// The global JSON backend options instance.
let options = SerdeJsonDefaults.options

/// Apply a configuration function to the global JSON backend options.
let configure (f: SerdeJsonOptions -> unit) = f options
