module Serde.FS.SourceGen.Tests.CollectionDiscoveryTests

open NUnit.Framework
open Serde.FS.SourceGen

// Issue #14: every closed built-in collection instantiation the generator can
// see must get a statically-typed codec registration in the consolidated JSON
// file, so nothing falls through to the reflection-based runtime factories under
// Native AOT / trimming.

let private generateJson (sourceFiles: (string * string) list) : string =
    let emitter = Serde.FS.Json.SourceGen.JsonCodeEmitter() :> Serde.FS.ISerdeCodeEmitter
    let result = SerdeGeneratorEngine.generate sourceFiles emitter
    Assert.That(result.Errors, Is.Empty, sprintf "Unexpected errors: %A" result.Errors)
    let consolidated =
        result.Sources |> List.tryFind (fun s -> s.HintName = "~SerdeJsonCodecs.json.g.fs")
    Assert.That(consolidated.IsSome, "Expected the consolidated ~SerdeJsonCodecs.json.g.fs to be emitted")
    consolidated.Value.Code

let private registrationOf (fqType: string) =
    sprintf "|> CodecRegistry.add (typeof<%s>," fqType

let private countOf (needle: string) (haystack: string) =
    let mutable count, idx = 0, haystack.IndexOf(needle)
    while idx >= 0 do
        count <- count + 1
        idx <- haystack.IndexOf(needle, idx + needle.Length)
    count

[<Test>]
let ``Record fields: list, array, Set, Map, seq, Result and nested collections get static codecs`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Pet = { Name: string }

[<Serde>]
type Owner = {
    Pets: Pet list
    Tags: string[]
    Lucky: Set<int>
    ByName: Map<string, Pet>
    Stream: seq<Pet>
    Outcome: Result<Pet, string>
    Nested: Pet list list
    Optional: Pet option list
}
"""
    let code = generateJson [ "/Domain.fs", source ]
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet list"))
    Assert.That(code, Does.Contain(registrationOf "string array"))
    Assert.That(code, Does.Contain(registrationOf "Set<int>"))
    Assert.That(code, Does.Contain(registrationOf "Map<string, MyApp.Pet>"))
    Assert.That(code, Does.Contain(registrationOf "seq<MyApp.Pet>"))
    Assert.That(code, Does.Contain(registrationOf "Result<MyApp.Pet, string>"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet list list"))
    // Option nested in a collection: both the option and the outer list are static.
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet option"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet option list"))
    Assert.That(code, Does.Contain("CollectionCodecs.listCodecOf<MyApp.Pet>"))
    Assert.That(code, Does.Contain("CollectionCodecs.seqCodecOf<MyApp.Pet>"))
    Assert.That(code, Does.Contain("CollectionCodecs.resultCodecOf<MyApp.Pet, string>"))

[<Test>]
let ``byte[] keeps its primitive base64 codec and is never re-registered`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Blob = { Data: byte[]; Chunks: byte[] list }
"""
    let code = generateJson [ "/Domain.fs", source ]
    Assert.That(code, Does.Not.Contain(registrationOf "byte array"))
    Assert.That(code, Does.Not.Contain("arrayCodecOf<byte>"))
    // ...but a list OF byte[] is a normal list whose element codec is the primitive.
    Assert.That(code, Does.Contain(registrationOf "byte array list"))

[<Test>]
let ``Union case fields and options nested in Result are discovered`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Pet = { Name: string }

[<Serde>]
type Event =
    | Adopted of Pet list
    | Checked of Result<Pet option, string>
    | Nothing
"""
    let code = generateJson [ "/Domain.fs", source ]
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet list"))
    Assert.That(code, Does.Contain(registrationOf "Result<MyApp.Pet option, string>"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet option"))

[<Test>]
let ``RpcApi method input and output types get static codecs, tupled inputs are not duplicated`` () =
    let source = """
namespace MyApp

open Serde.FS

type Product = { Id: int; Name: string }

[<RpcApi>]
type IOrderApi =
    abstract ListProducts : unit -> Async<Product list>
    abstract TryGet : int -> Async<Result<Product, string>>
    abstract Page : int * int -> Async<Product array>
    abstract Search : string seq -> Async<Map<string, Product>>
"""
    let code = generateJson [ "/Api.fs", source ]
    Assert.That(code, Does.Contain(registrationOf "MyApp.Product list"))
    Assert.That(code, Does.Contain(registrationOf "Result<MyApp.Product, string>"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Product array"))
    Assert.That(code, Does.Contain(registrationOf "seq<string>"))
    Assert.That(code, Does.Contain(registrationOf "Map<string, MyApp.Product>"))
    // RpcApiDiscovery already synthesizes the (int * int) tuple; the tuple pass must not add a second binding.
    Assert.That(countOf "intIntTupleJsonCodec :" code, Is.EqualTo 1)
    Assert.That(countOf (registrationOf "(int * int)") code, Is.EqualTo 1)

[<Test>]
let ``Root-level SerdeJson.serialize<T> and Serde.Serialize<T> type arguments are discovered`` () =
    let source = """
module MyApp.Program

open Serde.FS
open Serde.FS.Json

[<Serde>]
type Pet = { Name: string }

[<EntryPoint>]
let main _ =
    let json = SerdeJson.serialize<Pet list>([ { Name = "Rex" } ])
    let back = SerdeJson.deserialize<Map<int, Pet>>("[]")
    let s = Serde.Serialize<Pet array>([||])
    0
"""
    let code = generateJson [ "/Program.fs", source ]
    Assert.That(code, Does.Contain(registrationOf "MyApp.Program.Pet list"))
    Assert.That(code, Does.Contain(registrationOf "Map<int, MyApp.Program.Pet>"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Program.Pet array"))

[<Test>]
let ``Closed generic wrapper instantiations get static codecs while the open factory remains`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Wrapper<'T> = Wrapper of 'T

[<Serde>]
type Name = { Value: string }

[<Serde>]
type Person = { Wrapped: Wrapper<Name>; WrappedId: Wrapper<System.Guid> }
"""
    let code = generateJson [ "/Domain.fs", source ]
    Assert.That(code, Does.Contain("|> CodecRegistry.addFactory (typedefof<MyApp.Wrapper<_>>, wrapperCodecFactory)"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Wrapper<MyApp.Name>"))
    Assert.That(code, Does.Contain(registrationOf "MyApp.Wrapper<System.Guid>"))

[<Test>]
let ``Same collection referenced from several places is registered exactly once`` () =
    let source = """
namespace MyApp

open Serde.FS

[<Serde>]
type Pet = { Name: string }

[<Serde>]
type A = { Pets: Pet list }

[<Serde>]
type B = { More: Pet list; Other: Pet list option }
"""
    let code = generateJson [ "/Domain.fs", source ]
    Assert.That(countOf (registrationOf "MyApp.Pet list") code, Is.EqualTo 1)
    Assert.That(countOf "let private petListJsonCodec" code, Is.EqualTo 1)
    Assert.That(code, Does.Contain(registrationOf "MyApp.Pet list option"))
