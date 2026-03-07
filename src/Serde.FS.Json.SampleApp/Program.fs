module SampleApp

open System.Text.Json.Nodes
open Serde.FS
open Serde.FS.Json
open FSharp.SourceDjinn.TypeModel

// -----------------------------
// Basic Serde Types
// -----------------------------

[<Serde>]
type Color =
    | Red = 1
    | Green = 2
    | Blue = 3

[<Serde>]
type Address = { Street: string; City: string; Zip: string }

[<Serde>]
type Name = { Name: string }

// -----------------------------
// DU Examples (single + multi case)
// -----------------------------

[<Serde>]
type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Point

// -----------------------------
// Custom Converter Example
// -----------------------------

type UppercaseNameConverter() =
    interface ISerdeConverter<FancyName> with
        member _.Serialize(n: FancyName) =
            JsonValue.Create(n.Value.ToUpperInvariant()) :> JsonNode

        member _.Deserialize(node: JsonNode) =
            { Value = node.GetValue<string>().ToLowerInvariant() }

and [<Serde(Converter = typeof<UppercaseNameConverter>)>]
    FancyName = { Value: string }

// -----------------------------
// Generic Wrapper Example
// -----------------------------

[<Serde>]
type Wrapper<'T> = Wrapper of 'T

// -----------------------------
// Complex Person Example
// -----------------------------

[<Serde>]
type Pet =
    | Dog of Name
    | Cat of name: string

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
    Fancy: FancyName
    WrappedName: Wrapper<Name>
    WrappedGuid: Wrapper<System.Guid>
}

// -----------------------------
// Entry Point
// -----------------------------

[<EntryPoint>]
let run argv =
    SerdeJson.useAsDefault()

    // -----------------------------------------
    // 1. Generic Wrapper Example
    // -----------------------------------------
    let wrapperJson =
        Serde.Serialize<Wrapper<Person>>(
            Wrapper { 
                Name = "Jordan"
                Age = 0
                Address = None
                LuckyNumbers = Set.empty
                Colors = [||]
                Pets = []
                Position = 0.0, 0.0
                PetMap = Map.empty
                Shapes = []
                Fancy = { Value = "x" }
                WrappedName = Wrapper { Name = "x" }
                WrappedGuid = Wrapper System.Guid.Empty }
        )

    let wrapperRoundtrip : Wrapper<Person> =
        Serde.Deserialize wrapperJson

    printfn "Wrapper JSON: %s" wrapperJson
    printfn "Wrapper roundtrip: %A" wrapperRoundtrip

    // -----------------------------------------
    // 2. DU Examples (Circle, Point, Rectangle)
    // -----------------------------------------
    let shapes = [
        Shape.Circle 2.5
        Shape.Rectangle(10.0, 20.0)
        Shape.Point
    ]

    for shape in shapes do
        let shapeJson = Serde.Serialize shape
        let back : Shape = Serde.Deserialize shapeJson
        printfn "Shape JSON: %s" shapeJson
        printfn "Shape roundtrip: %A" back

    // -----------------------------------------
    // 3. Full Person Example
    // -----------------------------------------
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
        Shapes = shapes
        Fancy = { Value = "Jordan" }
        WrappedName = Wrapper { Name = "Wrapped" }
        WrappedGuid = Wrapper (System.Guid.Parse("12345678-1234-1234-1234-123456789abc"))
    }

    let personJson = Serde.Serialize person
    let personRoundtrip : Person = Serde.Deserialize personJson

    printfn "Person JSON: %s" personJson
    printfn "Person roundtrip: %A" personRoundtrip

    0
