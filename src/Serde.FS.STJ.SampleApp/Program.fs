module Program

open Serde.FS
open Serde.FS.STJ

[<Serde>]
type Person = { Name: string; Age: int; LuckyNumbers: int Set }

let run argv =
    SerdeStj.useAsDefault()
    let person = { Name = "John"; Age = 30; LuckyNumbers = Set [ 1; 2; 3 ] }
    let json = Serde.Serialize person
    printfn "Serialized: %s" json
    let deserialized: Person = Serde.Deserialize json
    printfn "Deserialized: %A" deserialized
    0

SerdeApp.entryPoint run
