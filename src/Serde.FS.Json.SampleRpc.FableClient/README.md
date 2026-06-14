# Serde.FS.Json.SampleRpc.FableClient

Fable + Lit demo that consumes the shared `IOrderApi` RPC contract by calling
the compile-time–generated Fable client (`IOrderApiFableClient.create`).

## Prerequisites

- .NET 8 SDK (or newer)
- Node.js 18+

## How it works

This Fable project installs the `Serde.FS.Fable` package. On every
build of this project, the package's MSBuild target scans the directly-
referenced `SampleRpc.Shared` project for `[<RpcApi>]` interfaces and writes
a typed Fable client into THIS project's `fable-generated/` folder. The
folder is auto-`.gitignore`d so generated artifacts never appear in git.

No setup ceremony: there's no "build server first" requirement, no
cross-project file writes, no opt-in attribute on the interface. Installing
the package IS the opt-in.

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

- `[<RpcApi>]` in `SampleRpc.Shared/Domain.fs` is the single source of truth
  for the contract. No additional attributes needed for client generation.
- Installing `Serde.FS.Fable` here triggers generation of
  `SampleRpc.FableClient/fable-generated/~IOrderApi.fable.g.fs` during this
  project's build.
- The generated file compiles under both .NET and Fable. Under .NET its body
  is dead code (Fable.Core's `[<Emit>]`, `jsNative`, `createObj`, etc. throw
  at runtime if invoked), so nothing calls it. Under Fable it becomes the
  browser-side RPC client.
- `App.fs` calls `IOrderApiFableClient.create "/"` and invokes the RPC methods
  like any interface implementation — no reflection, no hand-written JSON.

## Wiring

Zero manual wiring: `Serde.FS.Fable`'s `buildTransitive/Serde.FS.Fable.targets`
runs the generator during this project's build and auto-includes
`fable-generated/*.fs` as Compile items. Fable 5+ picks them up via
`FscCommandLineArgs`; `dotnet build` picks them up via the standard Compile
list.

The Shared project does NOT need a `Fable.Core` reference anymore — the
generated client lives in this Fable project, so `Fable.Core` belongs here.

## Not demonstrated

- `PlaceOrder` requires an `X-Api-Key` header for auth; the generated Fable
  client does not yet expose a headers API, so only the read-side methods are
  wired up here.
