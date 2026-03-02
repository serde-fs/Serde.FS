module Program

open Serde.FS
open Serde.FS.SystemTextJson
open FSharp.SourceDjinn.TypeModel

[<Serde>]
type Color =
    | Red = 1
    | Green = 2
    | Blue = 3

[<Serde>]
type Address = { Street: string; City: string; Zip: string }

type Name = { Name: string }

[<Serde>]
type Pet =
    | Dog of Name
    | Cat of name: string

[<Serde>]
type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Point

[<Serde>]
type Person = {
    Name: string
    Age: int
    Address: Address option
    LuckyNumbers: int Set
    Colors: Color[]
    Pets: Pet list
    Position: float * float
    PetMap: Map<string, Pet>
    Shapes: Shape list
}

[<EntryPoint>]
let run argv =
    SerdeStj.useAsDefault()
    let pets = [
        Dog { Name = "Fido" }
        Cat "Whiskers"
    ]
    let person = {
        Name = "John"
        Age = 30
        Address = Some { Street = "123 Main St"; City = "Springfield"; Zip = "12345" }
        LuckyNumbers = Set [ 1; 2; 3 ]
        Colors = [| Color.Red; Color.Green; Color.Blue |]
        Pets = pets
        Position = 10.5, 20.5
        PetMap = pets |> List.map (fun p -> match p with Dog n -> n.Name, p | Cat name -> name, p) |> Map.ofList
        Shapes = [ Shape.Circle(3.14); Shape.Rectangle(10.0, 20.0); Shape.Point ]
    }
    let json = Serde.Serialize person
    printfn "Serialized: %s" json
    let deserialized: Person = Serde.Deserialize json
    printfn "Deserialized: %A" deserialized

    let shapes = [ Shape.Circle(3.14); Shape.Rectangle(10.0, 20.0); Shape.Point ]
    for shape in shapes do
        let shapeJson = Serde.Serialize shape
        printfn "Shape: %s" shapeJson
        let back: Shape = Serde.Deserialize shapeJson
        printfn "Back: %A" back
    0
