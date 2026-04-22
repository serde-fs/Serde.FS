module App

open Lit
open Fable.Lit.Dsl
open SampleRpc.Shared

let private client = IOrderApiFableClient.create "/"

[<LitElement("sample-app")>]
let SampleApp() =
    let _ = LitElement.init (fun cfg -> cfg.useShadowDom <- false)
    let status, setStatus = Hook.useState ("Click a button to call the RPC API.": string)

    let call (label: string) (work: Async<string>) =
        async {
            setStatus (label + "...")
            try
                let! result = work
                setStatus (label + "\n\n" + result)
            with ex ->
                setStatus (sprintf "%s\n\nError: %s" label ex.Message)
        }
        |> Async.StartImmediate

    view {
        h1 { "Serde.FS Fable RPC Sample" }

        p {
            "Demo of the generated Fable client calling the ASP.NET "
            code { "IOrderApi" }
            " server. Make sure the server is running at "
            code { "http://localhost:5050" }
            "."
        }

        div {
            style "display: flex; flex-wrap: wrap; gap: 8px; margin: 1rem 0;"

            button {
                onClick (fun _ ->
                    call "GetProduct 42" (async {
                        let! p = client.GetProduct 42
                        return sprintf "%A" p
                    }))
                "GetProduct 42"
            }

            button {
                onClick (fun _ ->
                    call "TryGetProduct 42 (Ok)" (async {
                        let! r = client.TryGetProduct 42
                        return sprintf "%A" r
                    }))
                "TryGetProduct 42"
            }

            button {
                onClick (fun _ ->
                    call "TryGetProduct -1 (Error)" (async {
                        let! r = client.TryGetProduct -1
                        return sprintf "%A" r
                    }))
                "TryGetProduct -1"
            }

            button {
                onClick (fun _ ->
                    call "ListProducts" (async {
                        let! ps = client.ListProducts ()
                        return ps |> List.map (sprintf "%A") |> String.concat "\n"
                    }))
                "ListProducts"
            }
        }

        pre { status }
    }
