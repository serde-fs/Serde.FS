/// The .NET half of the round-trip suite (see RoundTrip.Cases).
///   write <file>   Writes the .NET encoding of every case, for RoundTrip.Fable.
///   verify <file>  Decodes RoundTrip.Fable's encoding of every case and checks
///                  it is the value that was sent. Exits non-zero on a mismatch.
module RoundTrip.Net.Program

open System.IO
open Serde.FS.Json
open RoundTrip

let private write (path: string) =
    let encoded = ResizeArray<string * string>()
    Cases.all
        { new Cases.ICaseRunner with
            member _.Run<'T when 'T : equality>(name, value: 'T, _) =
                encoded.Add(name, SerdeJson.serialize<'T> value) }
    File.WriteAllText(path, SerdeJson.serialize<(string * string) list> (List.ofSeq encoded))
    printfn "Wrote the .NET encoding of %d cases to %s" encoded.Count path
    0

let private verify (path: string) =
    let fromFable = SerdeJson.deserialize<(string * string) list> (File.ReadAllText path) |> Map.ofList
    let failures = ref 0
    let fail name (reason: string) =
        failures.Value <- failures.Value + 1
        eprintfn "FAIL  %s: %s" name reason
    Cases.all
        { new Cases.ICaseRunner with
            member _.Run<'T when 'T : equality>(name, value: 'T, _) =
                match fromFable.TryFind name with
                | None -> fail name "missing from the Fable output"
                | Some json ->
                    match (try Ok (SerdeJson.deserialize<'T> json) with ex -> Error ex.Message) with
                    | Ok decoded when decoded = value -> printfn "ok    %s" name
                    | Ok _ -> fail name $"Fable sent %s{json}, which decodes to a different value"
                    | Error message -> fail name $"Fable sent %s{json}, which fails to decode: %s{message}" }
    if failures.Value = 0 then
        printfn ".NET decoded every Fable request to the value sent (%d cases)" fromFable.Count
        0
    else
        eprintfn "%d case(s) failed" failures.Value
        1

[<Serde.FS.EntryPoint>]
let main argv =
    match argv with
    | [| "write"; path |] -> write path
    | [| "verify"; path |] -> verify path
    | _ ->
        eprintfn "usage: write <file> | verify <file>"
        2
