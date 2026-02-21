namespace Serde.FS.STJ

open System.Text.Json
open Serde.FS

type StjOptions(jsonOptions: JsonSerializerOptions) =
    interface ISerdeOptions
    member _.JsonOptions = jsonOptions
