/// The values the round-trip suite sends. Both programs walk the same list:
/// RoundTrip.Net encodes and decodes each value with the .NET codecs, and
/// RoundTrip.Fable sends it through the generated Fable client.
module RoundTrip.Cases

open System

/// Visits every case. `call` is the API method that echoes the value.
type ICaseRunner =
    abstract Run<'T when 'T : equality> : name: string * value: 'T * call: (IRoundTripApi -> 'T -> Async<'T>) -> unit

// Values stay at millisecond precision: a Fable DateTime / TimeSpan holds
// milliseconds, so finer ticks are not expected to survive.
let private utc = DateTime(2024, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc)

let all (r: ICaseRunner) =
    let numbers name v = r.Run(name, v, fun api -> api.EchoNumbers)
    numbers "numbers: max" {
        I8 = SByte.MaxValue; U8 = Byte.MaxValue; I16 = Int16.MaxValue; U16 = UInt16.MaxValue
        I32 = Int32.MaxValue; U32 = UInt32.MaxValue; I64 = Int64.MaxValue; U64 = UInt64.MaxValue
        F32 = Single.MaxValue; F64 = Double.MaxValue; Dec = Decimal.MaxValue }
    numbers "numbers: min" {
        I8 = SByte.MinValue; U8 = Byte.MinValue; I16 = Int16.MinValue; U16 = UInt16.MinValue
        I32 = Int32.MinValue; U32 = UInt32.MinValue; I64 = Int64.MinValue; U64 = UInt64.MinValue
        F32 = Single.MinValue; F64 = Double.MinValue; Dec = Decimal.MinValue }
    numbers "numbers: fractions" {
        I8 = -1y; U8 = 1uy; I16 = -1s; U16 = 1us; I32 = 0; U32 = 0u
        I64 = 9007199254740993L; U64 = 9007199254740993UL
        F32 = 1.1f; F64 = 0.1; Dec = 0.1m }
    numbers "numbers: exponents" {
        I8 = 0y; U8 = 0uy; I16 = 0s; U16 = 0us; I32 = 0; U32 = 0u; I64 = 0L; U64 = 0UL
        F32 = 1e-7f; F64 = 1e21; Dec = 0.0000000000000000000000000001m }
    numbers "numbers: tiny and whole" {
        I8 = 0y; U8 = 0uy; I16 = 0s; U16 = 0us; I32 = 0; U32 = 0u; I64 = 0L; U64 = 0UL
        F32 = 123.0f; F64 = Double.Epsilon; Dec = 1.50m }

    let text name v = r.Run(name, v, fun api -> api.EchoText)
    text "text: escapes and unicode" {
        S = "quote \" backslash \\ slash / newline \n tab \t nul \u0000 bell \u0007 é 中文 😀 </script>"
        C = 'é'; G = Guid "12345678-1234-1234-1234-123456789abc"; B = true }
    text "text: empty" { S = ""; C = '"'; G = Guid.Empty; B = false }

    let times name v = r.Run(name, v, fun api -> api.EchoTimes)
    times "times: utc" {
        Dt = utc; Dto = DateTimeOffset(2024, 2, 29, 13, 45, 30, 123, TimeSpan.FromMinutes 330.0)
        D = DateOnly(2024, 2, 29); T = TimeOnly(23, 59, 59, 999); Ts = TimeSpan(-1, -2, -3, -4, -500) }
    times "times: unspecified" {
        Dt = DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified)
        Dto = DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.FromHours -8.0)
        D = DateOnly(1, 1, 1); T = TimeOnly.MinValue; Ts = TimeSpan.Zero }
    times "times: local" {
        Dt = DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local); Dto = DateTimeOffset(utc)
        D = DateOnly(9999, 12, 31); T = TimeOnly(12, 0); Ts = TimeSpan(10000, 23, 59, 59, 999) }

    let optional name v = r.Run(name, v, fun api -> api.EchoOptional)
    optional "optional: none" { Maybe = None; Nested = None; Ack = None; Deep = None }
    optional "optional: some none" { Maybe = Some 0; Nested = Some None; Ack = Some (); Deep = Some (Some None) }
    optional "optional: some some" { Maybe = Some -1; Nested = Some (Some "x"); Ack = Some (); Deep = Some (Some (Some 7)) }

    let collections name v = r.Run(name, v, fun api -> api.EchoCollections)
    collections "collections: empty" {
        L = []; A = [||]; S = Set.empty; M = Map.empty; MS = Map.empty; Bytes = [||]
        Pair = (0, ""); Pairs = []; R = Ok 0; RS = Error "" }
    collections "collections: full" {
        L = [ 3; 1; 2 ]; A = [| "a"; ""; "c" |]; S = Set [ 3; 1; 2 ]
        M = Map [ -1, "minus"; 10, "ten"; 2, "two" ]
        MS = Map [ "b", Point; "a", Circle 1.5 ]
        Bytes = [| 0uy; 1uy; 127uy; 128uy; 255uy |]
        Pair = (Int32.MinValue, "x"); Pairs = [ 1, ("a", true); 2, ("b", false) ]
        R = Error "nope"; RS = Ok (Labeled ("l", Rect (1.0, 2.0))) }

    r.Run("shapes", [ Point; Circle 2.5; Rect (10.0, 20.0); Line (-1.0, 0.5); Labeled ("a", Labeled ("b", Point)) ], fun api -> api.EchoShapes)

    let account name v = r.Run(name, v, fun api -> api.EchoAccount)
    account "account: on hold" { Id = 7; Status = OnHold "audit"; Level = Level.Low; Owner = UserId "ada"; Product = { Value = 42 }; Color = Color.Green }
    account "account: active" { Id = 8; Status = Active; Level = Level.High; Owner = UserId ""; Product = { Value = -1 }; Color = Color.Red }

    r.Run("bare int64", Int64.MinValue, fun api -> api.EchoInt64)
    r.Run("bare decimal", -79228162514264337593543950335m, fun api -> api.EchoDecimal)
    r.Run("bare string", "line\nbreak 😀", fun api -> api.EchoString)
    r.Run("bare option: none", (None: Shape option), fun api -> api.EchoMaybeShape)
    r.Run("bare option: some", Some (Circle 0.5), fun api -> api.EchoMaybeShape)
    r.Run("bare tupled args", (3, "three"), fun api (a, b) -> api.EchoPair(a, b))
    r.Run("bare unit", (), fun api -> api.EchoUnit)
    r.Run("bare result: ok", (Ok 1: Result<int, string>), fun api -> api.EchoResult)
    r.Run("bare result: error", (Error "e": Result<int, string>), fun api -> api.EchoResult)
