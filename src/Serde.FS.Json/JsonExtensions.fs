// Extends Serde.FS Serde type with JSON backend-specific overloads that accept SerdeJsonOptions.
// This allows users to use the same Serde API while providing backend-specific options when needed.
namespace Serde.FS

open Serde.FS
open Serde.FS.Json

[<AutoOpen>]
module JsonSerdeExtensions =

    type Serde with

        static member inline Serialize(value: 'T, options: SerdeJsonOptions) =
            Serde.Serialize(value, options :> ISerdeOptions)

        static member inline Deserialize<'T>(json: string, options: SerdeJsonOptions) =
            Serde.Deserialize<'T>(json, options :> ISerdeOptions)
