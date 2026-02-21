namespace Serde.FS.STJ

open System.Text.Json
open Serde.FS

type StjBackend() =
    interface ISerdeBackend with
        member _.Serialize(value, options) =
            let opts =
                match options with
                | Some (:? StjOptions as o) -> o.JsonOptions
                | _ -> JsonSerializerOptions()
            JsonSerializer.Serialize(value, opts)

        member _.Deserialize(json, options) =
            let opts =
                match options with
                | Some (:? StjOptions as o) -> o.JsonOptions
                | _ -> JsonSerializerOptions()
            JsonSerializer.Deserialize<'T>(json, opts)
