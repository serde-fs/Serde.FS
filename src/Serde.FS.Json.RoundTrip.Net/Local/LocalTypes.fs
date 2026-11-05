/// A [<Serde>] type declared in a subfolder of the consuming project. The
/// generator must pick up local sources from @(Compile), not a scan of the
/// project root (#18). If it misses this file, `check` throws
/// SerdeCodecNotFoundException and the program exits non-zero.
module RoundTrip.Net.Local

open Serde.FS
open Serde.FS.Json

[<Serde>]
type LocalPerson = { Name: string; Age: int }

let check () =
    let value = { Name = "Test"; Age = 42 }
    let decoded = SerdeJson.deserialize<LocalPerson> (SerdeJson.serialize value)
    if decoded <> value then
        failwithf "Local subfolder type did not round-trip: %A" decoded
