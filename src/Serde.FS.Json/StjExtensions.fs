// Extends Serde.FS Serde type with System.Text.Json-specific overloads that accept SerdeStjOptions.
// This allows users to use the same Serde API while providing backend-specific options when needed.
namespace Serde.FS

open Serde.FS
open Serde.FS.Json

[<AutoOpen>]
module StjSerdeExtensions =

    type Serde with

        static member inline Serialize(value: 'T, options: SerdeStjOptions) =
            Serde.Serialize(value, options :> ISerdeOptions)

        static member inline Deserialize<'T>(json: string, options: SerdeStjOptions) =
            Serde.Deserialize<'T>(json, options :> ISerdeOptions)
