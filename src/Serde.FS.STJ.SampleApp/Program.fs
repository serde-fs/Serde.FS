open Serde.FS
open Serde.FS.STJ

[<Serde>]
type Person = { Name: string; Age: int }

SerdeStj.useAsDefault()

let person = { Name = "John"; Age = 30 }
let json = Serde.Serialize person
printfn "Serialized: %s" json

let deserialized: Person = Serde.Deserialize json
printfn "Deserialized: %A" deserialized
