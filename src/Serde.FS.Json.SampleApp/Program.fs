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

[<Serde>]
type Patch = {
    Nickname: string option option
    Ack: unit option
    Age: int option
    Deep: int option option option
}

[<Serde>]
type Blob = {
    Name: string option
    Payload: byte[]
    Thumb: byte[] option
    Ids: int[]
}

[<Serde>]
type Status =
    | [<SerdeRename("active")>] Active
    | [<SerdeRename("on_hold")>] OnHold of reason: string

[<Serde>]
type Level = | [<SerdeRename("lo")>] Low = 1 | High = 2

[<Serde>]
type Account = {
    [<SerdeRename("account_id")>] Id: int
    [<SerdeRename("display_name")>] Name: string option
    Status: Status
    Level: Level
}

[<Serde>]
type Timer = {
    Duration: System.TimeSpan
    Timeout: System.TimeSpan option
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

    // -----------------------------------------
    // 5. Nested options: Some None must survive
    // -----------------------------------------
    let patch = { Nickname = Some None; Ack = Some (); Age = Some 3; Deep = Some (Some None) }
    let patchJson = SerdeJson.serialize patch
    // Same JSON the Fable client writes for this value.
    let expectedPatchJson = """{"Nickname":{"Some":null},"Ack":{"Some":null},"Age":3,"Deep":{"Some":{"Some":null}}}"""
    if patchJson <> expectedPatchJson then
        eprintfn "Patch JSON: MISMATCH\n  expected %s\n  got      %s" expectedPatchJson patchJson
        exit 1
    roundtrip "Patch" patch patchJson (SerdeJson.deserialize<Patch> patchJson)

    let patches = [ patch; { Nickname = Some (Some "x"); Ack = None; Age = None; Deep = Some (Some (Some 7)) } ]
    let patchesJson = SerdeJson.serialize patches
    roundtrip "Patch list" patches patchesJson (SerdeJson.deserialize<Patch list> patchesJson)

    // -----------------------------------------
    // 6. Null references from interop: Some null is sent as None
    // -----------------------------------------
    let blob = { Name = Some null; Payload = null; Thumb = Some null; Ids = null }
    let blobJson = SerdeJson.serialize blob
    let expectedBlobJson = """{"Name":null,"Payload":null,"Thumb":null,"Ids":null}"""
    if blobJson <> expectedBlobJson then
        eprintfn "Blob JSON: MISMATCH\n  expected %s\n  got      %s" expectedBlobJson blobJson
        exit 1
    roundtrip "Blob" { blob with Name = None; Thumb = None } blobJson (SerdeJson.deserialize<Blob> blobJson)

    // -----------------------------------------
    // 7. TimeSpan: .NET constant ("c") format
    // -----------------------------------------
    let timer = { Duration = System.TimeSpan(-1, -2, -3, -4, -500); Timeout = Some (System.TimeSpan.FromMinutes 90.0) }
    let timerJson = SerdeJson.serialize timer
    let expectedTimerJson = """{"Duration":"-1.02:03:04.5000000","Timeout":"01:30:00"}"""
    if timerJson <> expectedTimerJson then
        eprintfn "Timer JSON: MISMATCH\n  expected %s\n  got      %s" expectedTimerJson timerJson
        exit 1
    roundtrip "Timer" timer timerJson (SerdeJson.deserialize<Timer> timerJson)

    // -----------------------------------------
    // 8. SerdeRename on record fields, union cases and enum cases
    // -----------------------------------------
    let accounts = [
        { Id = 7; Name = Some "Ada"; Status = OnHold "audit"; Level = Level.Low }
        { Id = 8; Name = None; Status = Active; Level = Level.High }
    ]
    let accountsJson = SerdeJson.serialize accounts
    let expectedAccountsJson =
        """[{"account_id":7,"display_name":"Ada","Status":{"Case":"on_hold","Fields":["audit"]},"Level":"lo"},"""
        + """{"account_id":8,"display_name":null,"Status":{"Case":"active","Fields":[]},"Level":"High"}]"""
    if accountsJson <> expectedAccountsJson then
        eprintfn "Account JSON: MISMATCH\n  expected %s\n  got      %s" expectedAccountsJson accountsJson
        exit 1
    roundtrip "Account list" accounts accountsJson (SerdeJson.deserialize<Account list> accountsJson)

    0
