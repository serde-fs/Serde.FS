#r "src/Serde.FS/bin/Debug/net8.0/Serde.FS.dll"
#r "src/Serde.FS.STJ/bin/Debug/net8.0/Serde.FS.STJ.dll"

open Serde.FS
open Serde.FS.STJ

StjBootstrap.useStj ()

type Person = { FName: string; LName: string }

let json = Serde.Serialize { FName = "Jordan"; LName = "Marr" }
printfn "%s" json

let person : Person = Serde.Deserialize json
printfn "%A" person
