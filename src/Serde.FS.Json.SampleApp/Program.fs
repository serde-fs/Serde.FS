module MyApp

open Serde.FS
open Serde.FS.Json

[<Serde>]
type Wrapper<'T> = Wrapper of 'T

[<Serde>]
type Person = { Name: string }

let value = Wrapper { Name = "Jordan" }


[<FSharp.SourceDjinn.TypeModel.EntryPoint>]
let main argv =

    SerdeJson.useAsDefault()

    let json = Serde.Serialize<Wrapper<Person>>(Wrapper { Name = "Jordan"})
    let person = Serde.Deserialize<Wrapper<Person>> json

    printfn "JSON: %s" json
    printfn "Person: %A" person

    0 