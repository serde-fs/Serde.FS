module Serde.FS.STJ.Tests

open NUnit.Framework
open Serde.FS

type Person = { FName: string; LName: string }

[<SetUp>]
let Setup () =
    Serde.FS.STJ.SerdeStj.register()

[<Test>]
let ``Serialize and deserialize a record`` () =
    let json = Serde.Serialize { FName = "Jordan"; LName = "Marr" }
    json |> string =! """{"FName":"Jordan","LName":"Marr"}"""

    let person : Person = Serde.Deserialize json
    person.FName =! "Jordan"
    person.LName =! "Marr"
