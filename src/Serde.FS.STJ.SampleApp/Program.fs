module Program

open Serde.FS
open Serde.FS.STJ

[<Serde>]
type Address = { Street: string; City: string }

[<Serde>]
type Person = { 
    Name: string
    Age: int
    Address: Address option
    LuckyNumbers: int Set 
}

let run argv =
    SerdeStj.useAsDefault()
    let person = { 
        Name = "John"
        Age = 30
        Address = Some { Street = "123 Main St"; City = "Springfield" }
        LuckyNumbers = Set [ 1; 2; 3 ] 
    }
    let json = Serde.Serialize person
    printfn "Serialized: %s" json
    let deserialized: Person = Serde.Deserialize json
    printfn "Deserialized: %A" deserialized
    0

SerdeApp.entryPoint run
