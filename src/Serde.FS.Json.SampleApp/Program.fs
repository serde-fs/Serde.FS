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
// Newtype Examples (issue #12)
// -----------------------------

// Case name = type name: the generated codec must reference the case as
// SampleApp.DocumentId, not SampleApp.DocumentId.DocumentId.
[<Serde>]
type DocumentId = DocumentId of string

// RequireQualifiedAccess variant keeps the Type.Case form.
[<Serde; RequireQualifiedAccess>]
type UserId = UserId of string

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

// Round-trip check that is Native AOT friendly: F# structural equality is
// compiled code, whereas `printfn "%A"` walks values through FSharp.Reflection,
// which is unavailable in a Native AOT binary. This app doubles as the AOT /
// trimming regression gate (`dotnet fsi debug-build.fsx -- -p aot`), so it must
// not use reflection itself, and it exits non-zero on any mismatch.
let private roundtrip (label: string) (original: 'T) (json: string) (decoded: 'T) =
    printfn "%s JSON: %s" label json
    if decoded = original then
        printfn "%s roundtrip: OK" label
    else
        eprintfn "%s roundtrip: MISMATCH" label
        exit 1

[<Serde.FS.EntryPoint>]
let run _argv =
    // -----------------------------------------
    // 1. Generic Wrapper Example
    // -----------------------------------------
    let wrapped =
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

    let wrapperJson = SerdeJson.serialize<Wrapper<Person>>(wrapped)
    let wrapperRoundtrip : Wrapper<Person> = SerdeJson.deserialize<Wrapper<Person>> wrapperJson
    roundtrip "Wrapper" wrapped wrapperJson wrapperRoundtrip

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
        roundtrip "Shape" shape shapeJson back

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
        Fancy = { Value = "jordan" }   // FancyNameCodec lower-cases on decode
        WrappedName = Wrapper { Name = "Wrapped" }
        WrappedGuid = Wrapper (System.Guid.Parse("12345678-1234-1234-1234-123456789abc"))
    }

    let personJson = SerdeJson.serialize person
    let personRoundtrip : Person = SerdeJson.deserialize personJson
    roundtrip "Person" person personJson personRoundtrip

    // Root-level collections (issue #14): the explicit type argument makes the
    // generator emit statically-typed codecs for these instantiations too.
    let petsJson = SerdeJson.serialize<Pet list> pets
    let petsRoundtrip = SerdeJson.deserialize<Pet list> petsJson
    roundtrip "Pet list" pets petsJson petsRoundtrip

    let stock = Map.ofList [ "widget", 3; "gadget", 0 ]
    let stockJson = SerdeJson.serialize<Map<string, int>> stock
    let stockRoundtrip = SerdeJson.deserialize<Map<string, int>> stockJson
    roundtrip "Map<string, int>" stock stockJson stockRoundtrip

    let outcome : Result<Person, string> = Ok person
    let outcomeJson = SerdeJson.serialize<Result<Person, string>> outcome
    let outcomeRoundtrip = SerdeJson.deserialize<Result<Person, string>> outcomeJson
    roundtrip "Result<Person, string>" outcome outcomeJson outcomeRoundtrip

    // -----------------------------------------
    // 4. Newtype Examples (issue #12)
    // -----------------------------------------
    let docId = DocumentId "doc-42"
    let docIdJson = SerdeJson.serialize docId
    let docIdRoundtrip : DocumentId = SerdeJson.deserialize docIdJson
    roundtrip "DocumentId" docId docIdJson docIdRoundtrip

    let userId = UserId.UserId "user-7"
    let userIdJson = SerdeJson.serialize userId
    let userIdRoundtrip : UserId = SerdeJson.deserialize userIdJson
    roundtrip "UserId" userId userIdJson userIdRoundtrip

    0
