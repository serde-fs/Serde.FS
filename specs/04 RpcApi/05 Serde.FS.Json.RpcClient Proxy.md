## 1. New concept: RpcClient factory

Add a small, backend‑agnostic API in `Serde.FS.Json`:

```fsharp
module Serde.FS.Json.RpcClient =

    val Create<'TApi> :
        baseUrl:string ->
        httpClient:System.Net.Http.HttpClient ->
        'TApi
```

Usage:

```fsharp
let http = new HttpClient()
let orders = Serde.FS.Json.RpcClient.Create<IOrderApi>("https://api.myapp.com", http)

let! product = orders.GetProduct(ProductId 42)
```

`Create<'TApi>` will be implemented by generated code per RPC interface.

---

## 2. Source generator: emit client proxy per RPC interface

In `Serde.FS.Json.SourceGen`, for each interface marked with `[<RpcApi>]` (e.g., `IOrderApi`), emit:

1. A concrete proxy type that implements the interface
2. A registration hook so `RpcClient.Create<'TApi>` can resolve it

### 2.1. Generated proxy type

For `IOrderApi`, generate something like:

```fsharp
type private IOrderApiClient(baseUrl: string, http: HttpClient) =
    // reuse existing generated serializers/deserializers from ~Rpc.IOrderApi.json.g.fs

    interface IOrderApi with

        member _.GetProduct(id: ProductId) : Task<Product> =
            task {
                // 1. serialize args
                let bodyJson = Serde.Json.serialize (IOrderApi_GetProduct_Args(id))

                // 2. compute URL
                let methodSegment = IOrderApi_Routing.getMethodSegment "GetProduct"
                let url = baseUrl.TrimEnd('/') + "/" + methodSegment

                // 3. send HTTP POST
                use content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
                use! resp = http.PostAsync(url, content)
                resp.EnsureSuccessStatusCode() |> ignore

                // 4. deserialize result
                let! respJson = resp.Content.ReadAsStringAsync()
                return Serde.Json.deserialize<IOrderApi_GetProduct_Result>(respJson).Value
            }
```

Notes:

- Reuse the existing generated argument/result types and serializers from the current RPC JSON emitter (no duplication).
- For `unit`/`Task` methods, generate appropriate `Task`/`Task<unit>` bodies.
- For sync methods (if any), either disallow or wrap `Task.Result`—your call, but async is preferred.

---

## 3. Routing: reuse Root, Version, UrlCase

You already compute Root, Version, and UrlCase on the server side. Mirror that logic into a small generated routing helper per interface.

Generate a module like:

```fsharp
module internal IOrderApi_Routing =

    let root : string = "IOrderApi"        // or from [<RpcApi(Root = "...")>]
    let versionSegment : string = ""       // or "/v2"
    let urlCase : UrlCase = UrlCase.Default

    let basePath = "/rpc/" + root + versionSegment

    let applyUrlCase (urlCase: UrlCase) (methodName: string) =
        match urlCase with
        | UrlCase.Default -> methodName
        | UrlCase.Kebab ->
            let chars =
                methodName
                |> Seq.collect (fun c ->
                    if Char.IsUpper c then seq { '-'; Char.ToLower c } else seq { c })
                |> Seq.toArray

            let s = String(chars)
            if s.StartsWith "-" then s.Substring 1 else s

    let getMethodSegment (methodName: string) =
        applyUrlCase urlCase methodName

    let getFullUrl baseUrl methodName =
        let methodSegment = getMethodSegment methodName
        baseUrl.TrimEnd('/') + basePath + "/" + methodSegment
```

The proxy then uses:

```fsharp
let url = IOrderApi_Routing.getFullUrl baseUrl "GetProduct"
```

This keeps server and client routing perfectly aligned.

---

## 4. Wire into RpcClient.Create<'TApi>

Have the source generator emit a small registration module per interface, plus a shared dispatcher.

### 4.1. Per‑interface registration

For `IOrderApi`:

```fsharp
module internal IOrderApi_ClientFactory =
    let create (baseUrl: string) (http: HttpClient) : obj =
        IOrderApiClient(baseUrl, http) :> obj
```

### 4.2. Shared dispatcher

In `Serde.FS.Json` (non‑generated), add:

```fsharp
module RpcClient =

    // populated by generated code via partial module / static field init
    let mutable private factories =
        System.Collections.Concurrent.ConcurrentDictionary<System.Type, string -> HttpClient -> obj>()

    let internal register<'TApi> (factory: string -> HttpClient -> obj) =
        factories[typeof<'TApi>] <- factory

    let Create<'TApi> (baseUrl: string) (http: HttpClient) : 'TApi =
        match factories.TryGetValue typeof<'TApi> with
        | true, f -> f baseUrl http :?> 'TApi
        | _ ->
            invalidOp $"No RPC client registered for type {typeof<'TApi>.FullName}. Did you enable Serde.FS.Json.SourceGen for this interface?"
```

Then, for each interface, the generator emits:

```fsharp
[<System.Runtime.CompilerServices.ModuleInitializer>]
let init () =
    Serde.FS.Json.RpcClient.register<IOrderApi>(IOrderApi_ClientFactory.create)
```

---

## 5. Behavior and defaults

- If `[<RpcApi>]` has no `Root`, use the exact interface name (`typeof<'TApi>.Name`).
- If no `Version`, omit the version segment.
- If no `UrlCase`, use `UrlCase.Default` (no method name transformation).
- If `UrlCase = UrlCase.Kebab`, apply the kebab‑case transform to method names on both server and client.

---

## 6. Scope and non‑goals for this spec

In scope:

- Generating a .NET client proxy for RPC interfaces
- Reusing existing JSON serializers and metadata
- Respecting Root, Version, UrlCase
- Providing `RpcClient.Create<'TApi>` as the public entry point

Out of scope (for now):

- Fable client generation
- Custom transports
- Advanced error mapping / retries / resilience policies

---
