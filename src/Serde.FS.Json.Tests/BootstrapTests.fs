module Serde.FS.Json.Tests.BootstrapTests

open System
open NUnit.Framework
open Serde.FS
open Serde.FS.Json
open Serde.FS.Json.Codec

/// Counts how many times Bootstrap ran its Init.
type CountingBootstrap() =
    static member val InitCount = 0 with get, set
    interface IEntryPointBootstrap with
        member _.Init() = CountingBootstrap.InitCount <- CountingBootstrap.InitCount + 1

/// Marker interface standing in for a generated [<RpcApi>] interface.
type IManualApi = interface end

/// Run via the direct-instance overload the generated entry point uses.
type DirectRunBootstrap() =
    static member val InitCount = 0 with get, set
    interface IEntryPointBootstrap with
        member _.Init() = DirectRunBootstrap.InitCount <- DirectRunBootstrap.InitCount + 1

/// Declared via the assembly-level [<SerdeBootstrap>] attribute below.
type AttributeDeclaredBootstrap() =
    static member val InitCount = 0 with get, set
    interface IEntryPointBootstrap with
        member _.Init() = AttributeDeclaredBootstrap.InitCount <- AttributeDeclaredBootstrap.InitCount + 1

// Mirrors what the source generator emits into consumer assemblies so
// Bootstrap can discover bootstraps from metadata instead of a type scan.
[<assembly: SerdeBootstrap(typeof<AttributeDeclaredBootstrap>)>]
do ()

/// Stands in for a generated <Iface>RpcBootstrap: registers a client factory
/// so RpcClient.create can find IManualApi via the bootstrap scan.
type ManualApiRpcBootstrap() =
    interface IEntryPointBootstrap with
        member _.Init() =
            RpcClient.register<IManualApi> (fun _ _ -> box { new IManualApi })

// -- Bootstrap tests --

[<Test>]
let ``Bootstrap Run assembly runs each implementor at most once`` () =
    Bootstrap.Run(typeof<CountingBootstrap>.Assembly)
    Bootstrap.Run(typeof<CountingBootstrap>.Assembly)
    // Even if another test triggered a scan first, the global once-per-type
    // tracking means Init ran exactly once across the whole test run.
    Assert.AreEqual(1, CountingBootstrap.InitCount)

[<Test>]
let ``Bootstrap Run with direct instances runs each type at most once`` () =
    Bootstrap.Run([| DirectRunBootstrap() :> IEntryPointBootstrap |])
    Bootstrap.Run([| DirectRunBootstrap() :> IEntryPointBootstrap |])
    // The once-per-type tracking is shared with the scan-based overloads.
    Bootstrap.Run(typeof<DirectRunBootstrap>.Assembly)
    Assert.AreEqual(1, DirectRunBootstrap.InitCount)

[<Test>]
let ``Bootstrap Run assembly runs attribute-declared bootstraps exactly once`` () =
    // The attribute-declared type is also visible to the legacy type scan;
    // the union of the two discovery paths must not run it twice.
    Bootstrap.Run(typeof<AttributeDeclaredBootstrap>.Assembly)
    Bootstrap.Run(typeof<AttributeDeclaredBootstrap>.Assembly)
    Assert.AreEqual(1, AttributeDeclaredBootstrap.InitCount)

[<Test>]
let ``RpcClient create falls back to bootstrap scan on factory miss`` () =
    use http = new System.Net.Http.HttpClient()
    // No explicit Init has run for ManualApiRpcBootstrap (unless the previous
    // test's assembly scan got there first - either way, no manual call here);
    // create should locate the factory via Bootstrap.
    let client = RpcClient.create<IManualApi> http "http://localhost"
    Assert.IsNotNull(client)

// -- registerCodecs merge tests --

let private stringlyCodec<'T> (decode: string -> 'T) (encode: 'T -> string) =
    { new IJsonCodec<'T> with
        member _.Encode v = JsonValue.String (encode v)
        member _.Decode json =
            match json with
            | JsonValue.String s -> decode s
            | _ -> failwith "Expected string" }
    |> JsonCodec.boxCodec

[<Test>]
let ``registerCodecs merges registrations from multiple calls`` () =
    let original = GlobalCodecRegistry.Current
    try
        let charCodec = stringlyCodec<char> (fun s -> s.[0]) string
        let sbyteCodec = stringlyCodec<sbyte> sbyte string

        SerdeJson.registerCodecs (fun reg -> reg |> CodecRegistry.add (typeof<char>, charCodec))
        SerdeJson.registerCodecs (fun reg -> reg |> CodecRegistry.add (typeof<sbyte>, sbyteCodec))

        // The first registration survived the second call (merge, not rebuild).
        Assert.IsTrue((CodecRegistry.tryFind typeof<char> GlobalCodecRegistry.Current).IsSome)
        Assert.IsTrue((CodecRegistry.tryFind typeof<sbyte> GlobalCodecRegistry.Current).IsSome)
        // Primitives seeded at startup are still present.
        Assert.IsTrue((CodecRegistry.tryFind typeof<int> GlobalCodecRegistry.Current).IsSome)
    finally
        GlobalCodecRegistry.Current <- original

[<Test>]
let ``registerCodecs is last-write-wins per type`` () =
    let original = GlobalCodecRegistry.Current
    try
        let upperCodec = stringlyCodec<char> (fun s -> s.[0]) (fun v -> (string v).ToUpper())
        let lowerCodec = stringlyCodec<char> (fun s -> s.[0]) (fun v -> (string v).ToLower())

        SerdeJson.registerCodecs (fun reg -> reg |> CodecRegistry.add (typeof<char>, upperCodec))
        SerdeJson.registerCodecs (fun reg -> reg |> CodecRegistry.add (typeof<char>, lowerCodec))

        let found = CodecRegistry.tryFind typeof<char> GlobalCodecRegistry.Current
        Assert.IsTrue(found.IsSome)
        Assert.AreEqual(JsonValue.String "a", found.Value.Encode(box 'A'))
    finally
        GlobalCodecRegistry.Current <- original
