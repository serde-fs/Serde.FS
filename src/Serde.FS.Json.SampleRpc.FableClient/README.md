# Serde.FS.Json.SampleRpc.FableClient

Fable + Lit demo that consumes the shared `IOrderApi` RPC contract by calling
the compile-time–generated Fable client (`IOrderApiFableClient.create`).

## Prerequisites

- .NET 8 SDK (or newer)
- Node.js 18+

## One-time setup

The generated Fable client is emitted into the Shared project's `obj/` directory
when the **ASP.NET server** is built. Build the server at least once so the
generated file exists:

```
dotnet build ../Serde.FS.Json.SampleRpc.Server
```

After any change to the `IOrderApi` contract, rebuild the server to regenerate.

## Run

Start the ASP.NET server in one terminal:

```
dotnet run --project ../Serde.FS.Json.SampleRpc.Server
```

Then start the Fable dev server in another terminal:

```
npm install
npm run dev
```

Open <http://localhost:3000>. Vite proxies `/rpc` → `http://localhost:5050`, so
the browser can call the server without CORS setup.

## What this demonstrates

- `[<GenerateFableClient>]` in `SampleRpc.Shared/Domain.fs` causes the server
  build to emit a ready-to-consume Fable client module at
  `SampleRpc.Shared/generated-fable/IOrderApi.fs`.
- The file compiles under both .NET and Fable. Under .NET its body is
  dead code (Fable.Core's `[<Emit>]`, `jsNative`, `createObj`, etc. throw at
  runtime if invoked), so nothing calls it. Under Fable it becomes the
  browser-side RPC client.
- `App.fs` calls `IOrderApiFableClient.create "/"` and invokes the RPC methods
  like any interface implementation — no reflection, no hand-written JSON.

## Wiring the generated file into Shared

The Shared project's `.fsproj` includes the generated directory with a glob:

```xml
<Compile Include="generated-fable\*.fs" Condition="Exists('generated-fable')" />
```

Fable's project cracker does not pick up `<Compile>` items injected via NuGet
`buildTransitive/*.targets` files, so this one-line include is required.

## Not demonstrated

- `PlaceOrder` requires an `X-Api-Key` header for auth; the generated Fable
  client does not yet expose a headers API, so only the read-side methods are
  wired up here.
