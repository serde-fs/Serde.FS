module Serde.FS.STJ.SerdeStj

open Serde.FS

let register () =
    Serde.DefaultBackend <- StjBackend() :> ISerdeBackend |> Some
