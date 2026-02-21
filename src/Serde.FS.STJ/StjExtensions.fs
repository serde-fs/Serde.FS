// Extends Serde.FS Serde type with System.Text.Json-specific overloads that accept StjOptions. 
// This allows users to use the same Serde API while providing backend-specific options when needed.
namespace Serde.FS

open Serde.FS
open Serde.FS.STJ

[<AutoOpen>]
module StjSerdeExtensions =

    type Serde with
        
        static member Serialize(value: 'T, options: StjOptions) =
            Serde.Serialize(value, options :> ISerdeOptions)

        static member Deserialize<'T>(json: string, options: StjOptions) =
            Serde.Deserialize<'T>(json, options :> ISerdeOptions)
