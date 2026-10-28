namespace RoundTrip

open System
open Serde.FS

// One type per area of the wire format, discovered through the [<RpcApi>]
// below. Every method echoes its argument, so each case checks one value in
// both directions: the Fable client's request against the .NET codecs, and
// the .NET encoding against the Fable client's decoder.

type Color = | Red = 1 | Green = 2

type Level = | [<SerdeRename("lo")>] Low = 1 | High = 2

type Shape =
    | Point
    | Circle of radius: float
    | Rect of width: float * height: float
    | Line of float * float
    | Labeled of label: string * shape: Shape

type UserId = UserId of string

[<Struct>]
type ProductId = { Value: int }

type Status =
    | [<SerdeRename("active")>] Active
    | [<SerdeRename("on_hold")>] OnHold of reason: string

type Numbers = {
    I8: sbyte
    U8: byte
    I16: int16
    U16: uint16
    I32: int
    U32: uint32
    I64: int64
    U64: uint64
    F32: float32
    F64: float
    Dec: decimal
}

type Text = {
    S: string
    C: char
    G: Guid
    B: bool
}

type Times = {
    Dt: DateTime
    Dto: DateTimeOffset
    D: DateOnly
    T: TimeOnly
    Ts: TimeSpan
}

type Optional = {
    Maybe: int option
    Nested: string option option
    Ack: unit option
    Deep: int option option option
}

type Collections = {
    L: int list
    A: string[]
    S: Set<int>
    M: Map<int, string>
    MS: Map<string, Shape>
    Bytes: byte[]
    Pair: int * string
    Pairs: (int * (string * bool)) list
    R: Result<int, string>
    RS: Result<Shape, string>
}

type Account = {
    [<SerdeRename("account_id")>] Id: int
    Status: Status
    Level: Level
    Owner: UserId
    Product: ProductId
    Color: Color
}

[<RpcApi>]
type IRoundTripApi =
    abstract EchoNumbers: Numbers -> Async<Numbers>
    abstract EchoText: Text -> Async<Text>
    abstract EchoTimes: Times -> Async<Times>
    abstract EchoOptional: Optional -> Async<Optional>
    abstract EchoCollections: Collections -> Async<Collections>
    abstract EchoShapes: Shape list -> Async<Shape list>
    abstract EchoAccount: Account -> Async<Account>
    // Bare values: the proxy encodes and decodes these without a codec module.
    abstract EchoInt64: int64 -> Async<int64>
    abstract EchoDecimal: decimal -> Async<decimal>
    abstract EchoString: string -> Async<string>
    abstract EchoMaybeShape: Shape option -> Async<Shape option>
    abstract EchoPair: int * string -> Async<int * string>
    abstract EchoUnit: unit -> Async<unit>
    abstract EchoResult: Result<int, string> -> Async<Result<int, string>>
