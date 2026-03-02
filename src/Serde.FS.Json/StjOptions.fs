namespace Serde.FS.Json

open Serde.FS

type SerdeStjOptions() =
    interface ISerdeOptions with
        /// Gets or sets strict mode. Mirrors Serde.Strict.
        member _.Strict
            with get () = Serde.Strict
            and set v = Serde.Strict <- v
        /// Gets or sets debug mode. Mirrors Serde.Debug.
        member _.Debug
            with get () = Serde.Debug
            and set v = Serde.Debug <- v
    member this.Strict
        with get () = (this :> ISerdeOptions).Strict
        and set v = (this :> ISerdeOptions).Strict <- v
    member this.Debug
        with get () = (this :> ISerdeOptions).Debug
        and set v = (this :> ISerdeOptions).Debug <- v

module internal SerdeStjDefaults =
    let options = SerdeStjOptions()
