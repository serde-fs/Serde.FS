/// The Fable half of the round-trip suite (see RoundTrip.Cases), run under
/// Node: `node Program.js <dotnet.json> <fable.json>`.
///
/// Each case goes through the generated client proxy, with `fetch` replaced
/// so that the call answers with the .NET encoding of the value. That checks
/// the client decodes what .NET wrote, and captures the request body the
/// client encoded, which is written out for RoundTrip.Net to decode.
module RoundTrip.Fable.Program

open Fable.Core
open Fable.Core.JsInterop
open RoundTrip
open SerdeGenerated.Fable

[<Import("readFileSync", "fs")>]
let private readFileSync (path: string, encoding: string) : string = jsNative

[<Import("writeFileSync", "fs")>]
let private writeFileSync (path: string, data: string) : unit = jsNative

[<Emit("process.argv[$0]")>]
let private argv (i: int) : string = jsNative

[<Emit("process.exitCode = $0")>]
let private setExitCode (code: int) : unit = jsNative

[<Emit("globalThis.fetch = $0")>]
let private installFetch (f: obj) : unit = jsNative

[<Emit("Promise.resolve($0)")>]
let private resolved (x: obj) : JS.Promise<obj> = jsNative

let mutable private response = ""
let mutable private requestBody = ""

installFetch (
    System.Func<string, obj, JS.Promise<obj>>(fun _url init ->
        requestBody <- unbox<string> init?body
        resolved (createObj [ "ok" ==> true; "text" ==> (fun () -> resolved (box response)) ])))

let private fromDotnet =
    JS.JSON.parse (readFileSync (argv 2, "utf8")) :?> obj[]
    |> Array.map (fun pair -> let p = unbox<obj[]> pair in unbox<string> p.[0], unbox<string> p.[1])
    |> Map.ofArray

let private api = IRoundTripApiFableClient.create "http://roundtrip"
let private sent = ResizeArray<string * string>()
let mutable private failures = 0
let mutable private encodedDifferently = 0

let private fail name (reason: string) =
    failures <- failures + 1
    printfn "FAIL  %s: %s" name reason

let private cases = ResizeArray<Async<unit>>()

Cases.all
    { new Cases.ICaseRunner with
        member _.Run<'T when 'T : equality>(name, value: 'T, call) =
            cases.Add(async {
                match fromDotnet.TryFind name with
                | None -> fail name "missing from the .NET output"
                | Some json ->
                    response <- json
                    requestBody <- ""
                    let! outcome = Async.Catch (call api value)
                    if requestBody <> "" then sent.Add(name, requestBody)
                    match outcome with
                    | Choice1Of2 decoded when decoded = value ->
                        if requestBody = json then printfn "ok    %s" name
                        else
                            // Allowed: the promise is that each side reads what
                            // the other writes, not byte-identical output.
                            encodedDifferently <- encodedDifferently + 1
                            printfn "ok    %s (encoded differently)\n        .NET  %s\n        Fable %s" name json requestBody
                    | Choice1Of2 decoded -> fail name (sprintf ".NET sent %s, which decodes to %A instead of %A" json decoded value)
                    | Choice2Of2 ex -> fail name $".NET sent %s{json}, which fails to decode: %s{ex.Message}"
            }) }

async {
    for case in cases do
        do! case
    let pairs = sent |> Seq.map (fun (name, body) -> box [| name; body |]) |> Array.ofSeq
    writeFileSync (argv 3, JS.JSON.stringify pairs)
    printfn "Fable decoded %d of %d cases; %d encoded differently from .NET" (cases.Count - failures) cases.Count encodedDifferently
    if failures > 0 then setExitCode 1
}
|> Async.StartImmediate
