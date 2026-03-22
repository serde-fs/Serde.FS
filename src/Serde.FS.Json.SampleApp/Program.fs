module SampleApp

open Serde.FS
open Serde.FS.Json
open Serde.FS.Json.Codec

// -----------------------------
// Basic Serde Types
// -----------------------------

[<Serde>]
type Color = | Red = 1 | Green = 2 | Blue = 3

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

// Custom codec that uppercases on encode and lowercases on decode
type FancyNameCodec() =
    interface IJsonCodec<FancyName> with
        member _.Encode(n: FancyName) =
            JsonValue.String(n.Value.ToUpperInvariant())

        member _.Decode(json: JsonValue) =
            match json with
            | JsonValue.String s -> { Value = s.ToLowerInvariant() }
            | _ -> failwith "Expected JSON string for FancyName"

// Attach the codec to the type

and 
    [<Serde(Codec = typeof<FancyNameCodec>)>]
    FancyName = { Value : string }

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
// Mock IOrderApi Implementation
// -----------------------------

open SampleRpc

type OrderApiImpl() =
    interface IOrderApi with
        member _.GetProduct id =
            async {
                return {
                    Id = { Value = id }
                    Name = $"Product #{id}"
                    Price = decimal id * 9.99m
                    Tags = [ "sample" ]
                } : Product
            }

        member _.PlaceOrder order =
            async {
                let totalItems = order.Lines |> List.sumBy (fun l -> l.Quantity)
                let totalPrice = order.Lines |> List.sumBy (fun l -> decimal l.Quantity * l.Product.Price)
                return { OrderId = order.Id; TotalItems = totalItems; TotalPrice = totalPrice }
            }

        member _.ListProducts() =
            async {
                return [
                    { Id = { Value = 1 }; Name = "Widget"; Price = 9.99m; Tags = ["sale"] }
                    { Id = { Value = 2 }; Name = "Gadget"; Price = 24.50m; Tags = [] }
                ]
            }

// -----------------------------
// Console Mode
// -----------------------------

let runConsole () =
    // -----------------------------------------
    // 1. Generic Wrapper Example
    // -----------------------------------------
    let wrapperJson =
        SerdeJson.serialize<Wrapper<Person>>(
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
        SerdeJson.deserialize wrapperJson

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
        let shapeJson = SerdeJson.serialize shape
        let back : Shape = SerdeJson.deserialize shapeJson
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

    let personJson = SerdeJson.serialize person
    let personRoundtrip : Person = SerdeJson.deserialize personJson

    printfn "Person JSON: %s" personJson
    printfn "Person roundtrip: %A" personRoundtrip

    // -----------------------------------------
    // 4. RpcApi Discovery Example (SampleRpc)
    // -----------------------------------------
    let order : SampleRpc.Order = {
        Id = 1
        Lines = [
            { Product = { Id = { Value = 42 }; Name = "Widget"; Price = 9.99m; Tags = ["sale"; "new"] }
              Quantity = 3 }
            { Product = { Id = { Value = 99 }; Name = "Gadget"; Price = 24.50m; Tags = [] }
              Quantity = 1 }
        ]
        Notes = Some "Rush delivery"
    }

    let orderJson = SerdeJson.serialize order
    let orderRoundtrip : SampleRpc.Order = SerdeJson.deserialize orderJson

    printfn "Order JSON: %s" orderJson
    printfn "Order roundtrip: %A" orderRoundtrip

    let summary : SampleRpc.OrderSummary = { OrderId = 1; TotalItems = 4; TotalPrice = 54.47m }
    let summaryJson = SerdeJson.serialize summary
    let summaryRoundtrip : SampleRpc.OrderSummary = SerdeJson.deserialize summaryJson

    printfn "Summary JSON: %s" summaryJson
    printfn "Summary roundtrip: %A" summaryRoundtrip

// -----------------------------
// Web Mode
// -----------------------------

open Microsoft.AspNetCore.Builder
open Serde.FS.Json.AspNet

let runWeb (argv: string[]) =
    let builder = WebApplication.CreateBuilder(argv)
    let app = builder.Build()

    app.MapRpcApi<SampleRpc.IOrderApi>(OrderApiImpl()) |> ignore
    app.MapGet("/", System.Func<string>(fun () -> "Serde.FS.Json SampleApp — RPC endpoints at /rpc/{method}")) |> ignore

    printfn "Starting web server..."
    printfn "Try: curl -X POST http://localhost:5050/rpc/ListProducts -d '\"\"' -H 'Content-Type: application/json'"
    app.Run()

// -----------------------------
// Entry Point
// -----------------------------

[<EntryPoint>]
let run argv =
    if argv |> Array.contains "--web" then
        runWeb (argv |> Array.filter (fun a -> a <> "--web"))
        0
    else
        runConsole ()
        0
