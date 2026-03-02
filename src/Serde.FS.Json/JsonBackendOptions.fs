namespace Serde.FS.Json

open Serde.FS

type SerdeJsonOptions() =
    interface ISerdeOptions

module internal SerdeJsonDefaults =
    let options = SerdeJsonOptions()
