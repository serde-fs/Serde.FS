# **Serde.FS — Strict, Deterministic Serialization for F#**

[![Serde.FS](https://img.shields.io/nuget/vpre/Serde.FS.svg?label=Serde.FS)](https://www.nuget.org/packages/Serde.FS/)
[![Serde.FS.Json](https://img.shields.io/nuget/vpre/Serde.FS.Json.svg?label=Serde.FS.Json)](https://www.nuget.org/packages/Serde.FS.Json/)
[![Serde.FS.AspNet](https://img.shields.io/nuget/vpre/Serde.FS.AspNet.svg?label=Serde.FS.AspNet)](https://www.nuget.org/packages/Serde.FS.AspNet/)
[![Serde.FS.Fable](https://img.shields.io/nuget/vpre/Serde.FS.Fable.svg?label=Serde.FS.Fable)](https://www.nuget.org/packages/Serde.FS.Fable/)

Serde.FS is a reflection‑free, compile‑time validated serialization and RPC framework for F#.
It brings Rust‑style determinism to .NET and adds a **zero‑boilerplate RPC layer** on top — your ASP.NET server, .NET client, and Fable browser client are all generated from a single `[<RpcApi>]` interface.

---

## 🚀 RPC in Three Blocks

### Shared.fsproj
```fsharp
[<RpcApi>]
type IOrderApi =
    abstract GetProduct : ProductId -> Async<Product>
    abstract PlaceOrder : Order -> Async<OrderSummary>
```

### Server.fsproj (ASP.NET)
```fsharp
app.MapRpcApi<IOrderApi>(OrderApi())
```

### Client — pick your stack

**.NET client (HttpClient):**
```fsharp
use http = new HttpClient()
let api = RpcClient.create<IOrderApi> http "http://localhost:5050"
let! product = api.GetProduct(ProductId 42)
```

**Fable client (browser):**
```fsharp
open SerdeGenerated.Fable

let api = IOrderApiFableClient.create "/"
let! product = api.GetProduct(ProductId 42)
```

That’s the entire workflow:
**Define an interface → Implement it → Call it from any F# runtime.**

Routing, serialization, and client code are all generated at compile time by the same deterministic engine that powers Serde.FS — no reflection, no runtime inference, no DTO drift.

---

## 📦 Package Map

Pair one column of packages with your three‑project F# solution:

| Project | .NET‑client stack       | Fable‑client stack       |
|---------|-------------------------|--------------------------|
| **Shared** (interface + DTOs) | `Serde.FS` | `Serde.FS` |
| **Server** (ASP.NET) | `Serde.FS.AspNet` | `Serde.FS.AspNet` |
| **Client** | `Serde.FS.Json` | `Serde.FS.Fable` |

What each package brings:

| Package | What it adds |
|---------|--------------|
| **Serde.FS** | Core attributes (`[<Serde>]`, `[<RpcApi>]`) and runtime metadata. Install on any project that declares serializable types or RPC interfaces. |
| **Serde.FS.Json** | Deterministic, reflection‑free JSON backend. Use it standalone for `SerdeJson.serialize`/`deserialize`, or on a .NET client project to get `RpcClient.create<T>`. |
| **Serde.FS.AspNet** | Adds `app.MapRpcApi<T>(impl)` for ASP.NET endpoints. Builds directly on ASP.NET Core's minimal hosting — no Giraffe, Saturn, or controllers required. Transitively brings `Serde.FS.Json`, so installing this on the server is enough. |
| **Serde.FS.Fable** | Installing this on a Fable project turns ON Fable client generation: every build scans directly‑referenced projects for `[<RpcApi>]` interfaces and writes a typed proxy into `fable-generated/` (auto‑included in compilation). |

---

## 🚀 Getting Started

The smallest possible Serde.FS RPC setup follows the classic three‑project full‑stack layout:

```
SampleRpc/
  Shared/
  Server/
  Client/        ← .NET client
  WebFable/      ← (optional) Fable browser client
```

You can use *either* `Client/` or `WebFable/` — or both, against the same `Shared` interface. They’re fully independent.

---

### 📁 1. Create the Shared project

Define your RPC interface and domain types.

```bash
dotnet new classlib -lang F# -n Shared
dotnet add package Serde.FS
```

```fsharp
namespace Shared

open Serde.FS

// DTOs — no [<Serde>] needed, discovered via [<RpcApi>] interface.

[<Struct>]
type ProductId = ProductId of int

type Product =
    { Id: ProductId
      Name: string }

[<RpcApi>]
type IOrderApi =
    abstract GetProduct : ProductId -> Async<Product>
```

The interface lives in this project. The server, the .NET client, and the Fable client all reference it.

See: [SampleRpc.Shared/Domain.fs](src/Serde.FS.Json.SampleRpc.Shared/Domain.fs)

---

### 🖥️ 2. Create the Server project

A minimal ASP.NET RPC server:

```bash
dotnet new web -lang F# -n Server
dotnet add package Serde.FS.AspNet
```

```fsharp
open Shared
open Microsoft.AspNetCore.Builder
open Serde.FS.AspNet

type OrderApi() =
    interface IOrderApi with
        member _.GetProduct(ProductId id) =
            async { return { Id = ProductId id; Name = $"Product %d{id}" } }

[<Serde.FS.EntryPoint>]
let main argv =
    let builder = WebApplication.CreateBuilder(argv)
    let app = builder.Build()

    app.MapRpcApi<IOrderApi>(OrderApi()) |> ignore

    app.Run()
    0
```

No auth, no policies, no extra endpoints — just a clean RPC server.

> **No web framework required.** `MapRpcApi` is just an ASP.NET Core endpoint‑routing extension, so an RPC BFF needs nothing beyond `Microsoft.NET.Sdk.Web` + `Serde.FS.AspNet` — no Giraffe, no Saturn, no controllers. And because it's plain ASP.NET, you can add minimal‑API endpoints (`app.MapGet "/health" ...`) right alongside `MapRpcApi` whenever you also need a REST surface — they coexist as two separate surfaces on the same host.

> **Host it anywhere.** As of `1.0.0-beta.4`, `MapRpcApi` self‑initializes the generated codec and RPC registrations, so it works even in hosts where the source‑generated entry point never runs — a desktop shell embedding Kestrel in‑process, a `WebApplicationFactory` test host, an AutoCAD/Revit add‑in, or a C# host referencing your API assembly as a library. `[<Serde.FS.EntryPoint>]` is still recommended for apps that also serialize outside RPC before the first `MapRpcApi` call (see [Custom EntryPoint](#-custom-entrypoint-for-cli-apps)).

See: [SampleRpc.Server/Program.fs](src/Serde.FS.Json.SampleRpc.Server/Program.fs)

---

### 💻 3a. Create the .NET client (optional)

For a console app, desktop client, or any .NET service that needs to call the API:

```bash
dotnet new console -lang F# -n Client
dotnet add package Serde.FS.Json
```

```fsharp
open Shared
open Serde.FS.Json

[<Serde.FS.EntryPoint>]
let main _ =
    async {
        use http = new HttpClient()
        let orders = RpcClient.create<IOrderApi> http "http://localhost:5000"

        let! product = orders.GetProduct(ProductId 42)
        printfn $"Product: %A{product}"
    }
    |> Async.RunSynchronously

    0
```

The client proxy is generated at compile time — no reflection, no runtime inference, no DTO drift. Like `MapRpcApi`, `RpcClient.create` self‑initializes on first use, so it works in hosts without the generated entry point.

See: [SampleRpc.Client/Program.fs](src/Serde.FS.Json.SampleRpc.Client/Program.fs)

---

### 🌐 3b. Create the Fable client (optional)

For a browser app (Lit, Feliz, Elmish, etc.). Install `Serde.FS.Fable` on the Fable project — its presence is the opt‑in. No attribute needed on the interface; every build scans directly‑referenced projects for `[<RpcApi>]` interfaces.

```xml
<!-- WebFable/WebFable.fsproj -->
<PackageReference Include="Serde.FS.Fable" Version="1.0.0-..." />
<PackageReference Include="Fable.Core" Version="5.0.0" />
```

After the next build, the generated client appears at `WebFable/fable-generated/~IOrderApi.fable.g.fs` (auto‑included as a `Compile` item — no manual `.fsproj` editing). Consume it like any normal interface:

```fsharp
open Shared
open SerdeGenerated.Fable

let api = IOrderApiFableClient.create "/"

async {
    let! product = api.GetProduct(ProductId 42)
    printfn $"Product: %A{product}"
}
```

The `fable-generated/` folder is auto‑`.gitignore`d (the generator drops a self‑ignoring marker file), so generated artifacts never show up in `git status`.

See: a full end‑to‑end example (ASP.NET server + Lit‑based Fable web client) lives under [src/Serde.FS.Json.SampleRpc.FableClient](src/Serde.FS.Json.SampleRpc.FableClient).

---

### 🎉 That’s the entire workflow

**Define an interface → Implement it → Call it from .NET, the browser, or both.**

All routing, serialization, and client code is generated at compile time by the same deterministic engine.

---

## 📊 Serde.FS.Fable vs Fable.Remoting — Comparison Matrix

| **Aspect** | **Serde.FS.Fable** | **Fable.Remoting** |
|-----------|--------------------------|---------------------|
| **Bundle Size Behavior** | Per‑method generated codecs; scales with API surface. Compresses extremely well; unused methods tree‑shake. | Mostly constant (fixed reflection engine). |
| **Runtime Performance** | Straight‑line generated codecs; no reflection; very fast per call. | Reflection + dynamic codecs; slower per call. |
| **Architecture** | Fully static, deterministic, AOT/WASM‑safe. | Runtime‑driven, reflection‑based. |
| **Modularization** | Define multiple [<RpcApi>] interfaces; each consumer imports only the APIs it references. | Define multiple API records/modules; clients can consume them separately. |
| **Type Safety** | End‑to‑end static types; generated codecs match your domain exactly. | Static RPC signatures but runtime serialization. |
| **Debuggability** | Generated code is explicit and inspectable; failures are deterministic. | Reflection paths can be harder to trace; failures may occur at runtime. |
| **WASM Compatibility** | Fully compatible (no reflection). | Reflection may be unsupported or limited in some WASM targets. |
| **Initial Setup** | Define API interface in Shared, mark with `[<RpcApi>]`, register it on the server, and instantiate the generated client on the Fable side. Codegen runs automatically on build. | Define API record in Shared, register it on the server, and instantiate the Remoting client on the Fable side. |

---

## ⚙️ Customizing RPC Routing

Override the URL root and case style on the interface:

```fsharp
[<RpcApi(Root = "orders", UrlCase = UrlCase.Kebab)>]
type IOrderApi =
    abstract GetProduct : ProductId -> Async<Product>
```

---

## 🧰 Serialization (Standalone Use)

If you’re not building RPC and just want a fast, deterministic JSON serializer for F# types:

### 1. Install the JSON backend

```bash
dotnet add package Serde.FS.Json
```

### 2. Annotate your types

```fsharp
open Serde.FS

[<Serde>]
type Person = {
    Name : string
    Age  : int
}
```

### 3. Serialize and deserialize

```fsharp
open Serde.FS.Json

let json = SerdeJson.serialize { Name = "Gustens"; Age = 14 }
let person : Person = SerdeJson.deserialize json
```

That’s the entire workflow: **opt in → generate → serialize**.

### 4. Add EntryPoint

If you’re using Serde.FS in an app that needs an entry point, use the special `Serde.FS.EntryPoint` attribute:

```fsharp
[<Serde.FS.EntryPoint>]
let main argv = ...
```

See [🏁 Custom EntryPoint for CLI apps](#-custom-entrypoint-for-cli-apps) below for the full explanation.

---

## ✨ Key Features

- **Strict, Serde‑style serialization** — Only `[<Serde>]` types participate.
- **Compile‑time validation** — Nested types must also be annotated.
- **Deterministic code generation** — Stable, predictable serializers.
- **Fast runtime backend** — F# source‑generated code, no reflection.
- **Custom converters** — Override behavior per‑type without weakening strictness.
- **Backend‑agnostic core** — JSON today, TOML/YAML tomorrow.
- **End‑to‑end RPC** — One `[<RpcApi>]` interface generates server routes, a .NET client, and a Fable browser client.

---

## 🔒 Strictness Model

Serde.FS enforces strictness at two levels:

### **1. Root strictness (runtime)**
Serializing a type without `[<Serde>]` throws a strict violation.

### **2. Nested strictness (build‑time)**
If a Serde‑annotated type contains a nested type that lacks `[<Serde>]`, the generator fails the build.

This mirrors Rust Serde:

> If a nested type doesn’t derive Serialize, the parent type cannot derive Serialize.

---

## 🧩 Custom Converters

Serde.FS supports attribute‑level codecs that override serialization for specific types.

### 1. Implement a custom codec

```fsharp
open Serde.FS
open Serde.FS.Json.Codec

// Custom codec that uppercases on encode and lowercases on decode
type FancyNameCodec() =
    interface IJsonCodec<FancyName> with
        member _.Encode(n: FancyName) =
            JsonValue.String(n.Value.ToUpperInvariant())

        member _.Decode(json: JsonValue) =
            match json with
            | JsonValue.String s -> { Value = s.ToLowerInvariant() }
            | _ -> failwith "Expected JSON string for FancyName"
```

### 2. Attach it to a type

```fsharp
and
    [<Serde(Codec = typeof<FancyNameCodec>)>]
    FancyName = { Value : string }
```

### 3. Use it normally

```fsharp
let fancy = { Value = "Jordan" }
let json = SerdeJson.serialize fancy
// => "JORDAN"

let back : FancyName = SerdeJson.deserialize json
// => { Value = "jordan" }
```

Converters are explicit, compile‑time validated, and do not introduce fallback or reflection.

---

## 🏁 Custom EntryPoint for CLI apps
F# requires the real `[<EntryPoint>]` function to appear last in compilation order.
Because Serde.FS uses source generation, it cannot safely generate the real entry point directly.

To solve this, mark your intended entry point with:

```fsharp
[<Serde.FS.EntryPoint>]
let main argv = ...
```

Serde.FS will generate the actual `[<EntryPoint>]` wrapper in a separate file so it appears in the correct place in the compilation order.

### Hosting without the generated entry point

Some hosts never run your assembly's entry point at all: a WPF/Photino desktop shell that starts Kestrel in‑process, an AutoCAD/Revit add‑in loaded into the host application, a `WebApplicationFactory` integration test, or a C# app referencing your F# API assembly as a library.

As of `1.0.0-beta.4` this just works for RPC: `MapRpcApi` and `RpcClient.create` run the generated registrations on first use. If you need registrations *before* that — for example, standalone `SerdeJson.serialize` calls during startup — run them explicitly:

```fsharp
// Run every generated bootstrap in the loaded assemblies:
Serde.FS.Bootstrap.Run()

// Or target a specific assembly (Init failures propagate):
Serde.FS.Bootstrap.Run(typeof<MyApi>.Assembly)
```

`Bootstrap.Run` is idempotent — each generated bootstrap runs at most once per process, no matter how many times or from how many threads it's called.

---

## 🧱 Design Philosophy

- **Explicitness** — Only annotated types participate.
- **Determinism** — No runtime inference or fallback.
- **Compile‑time validation** — Errors surface early.
- **Backend independence** — Metadata is backend‑agnostic.

---

## 🧠 Mental Model

Serde.FS is not a general‑purpose .NET serializer. It is a **compile‑time, explicit, deterministic system** inspired by Rust Serde.

- Only annotated types participate.
- No runtime inference or fallback.
- Errors surface early and predictably.
- Backends follow Serde semantics, not their own.

Ideal for stable domain models, configuration files, deterministic logs, RPC contracts, and interop formats.

Not designed for dynamic JSON, schema‑drifting storage, partial deserialization, or runtime‑mutable behavior.

---

## 🎯 When to Use Serde.FS.Json

Serde.FS.Json is a deterministic, reflection‑free JSON backend designed for:

- compile‑time validated serialization
- stable, schema‑aware encoding
- AOT/WASM‑friendly performance
- powering the Serde.FS RPC platform (server, .NET client, Fable client)

If you need highly flexible or dynamic JSON formats, a runtime‑configured library may be a better fit.

---

## 🔮 Powered by FSharp.SourceDjinn
Serde.FS.Json is powered by [FSharp.SourceDjinn](https://github.com/serde-fs/FSharp.SourceDjinn), a lightweight source generator engine.
