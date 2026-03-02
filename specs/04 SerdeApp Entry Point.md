## Feature overview

**Goal:**  
Enable F# single‑project console apps that use Serde.FS analyzers by letting users **register an entry point callback**, and having Serde.FS **generate the real `[<EntryPoint>]`** as the last compiled module.

**Key idea:**  
- User calls `SerdeApp.registerEntryPoint` with a `string[] -> int` function.
- Source generator detects this and emits a small entry‑point shim:
  - `[<EntryPoint>] let main argv = SerdeApp.invokeRegisteredEntryPoint argv`
- This generated module is always last (analyzer output), satisfying F#’s “entry point must be last” rule.

No extra project. No custom MSBuild. Single‑line opt‑in.

---

## Public API changes (Serde.FS core)

Add a new module to **Serde.FS core** (not backend‑specific):

```fsharp
namespace Serde.FS

module SerdeApp =

    /// Registers the application's entry point callback.
    /// This is intended for use in simple console apps.
    val registerEntryPoint : (string[] -> int) -> unit

    /// Called from generated code to invoke the registered entry point.
    /// If no entry point is registered, returns 0.
    val internal invokeRegisteredEntryPoint : string[] -> int
```

Suggested implementation (Claude can refine):

```fsharp
namespace Serde.FS

module SerdeApp =

    let mutable private entryPoint : (string[] -> int) option = None

    let registerEntryPoint fn =
        entryPoint <- Some fn

    let internal invokeRegisteredEntryPoint argv =
        match entryPoint with
        | Some fn -> fn argv
        | None -> 0
```

Constraints:

- `registerEntryPoint` is **public**.
- `invokeRegisteredEntryPoint` is **internal** (only generator‑emitted code should call it).
- Only one entry point is supported per assembly; last registration wins (or generator can enforce “only one”).

---

## Generator behavior (Serde.FS.SourceGen)

### 1. Detection

The generator must detect whether the user has opted in by calling:

```fsharp
SerdeApp.registerEntryPoint <something>
```

Requirements:

- Detect calls to `Serde.FS.SerdeApp.registerEntryPoint`.
- Extract the function expression passed as the argument (but **do not** need to understand its body).
- It’s enough to know “at least one call exists in this compilation.”

For v1, we can assume:

- The call is at module top level (not inside a function).
- The argument is a value/function name (e.g., `run`), not an arbitrary lambda. (Claude can relax this later if easy.)

### 2. Emission

If at least one `SerdeApp.registerEntryPoint` call is found:

- Emit a single generated file with:

```fsharp
module Serde.Generated.EntryPoint

open Serde.FS

[<EntryPoint>]
let main argv =
    SerdeApp.invokeRegisteredEntryPoint argv
```

Details:

- Module name: `Serde.Generated.EntryPoint` (or similar, but stable).
- Must include `[<EntryPoint>]` attribute.
- Must call `SerdeApp.invokeRegisteredEntryPoint argv`.
- No assumptions about user modules/namespaces.
- No references to user code directly—only to `SerdeApp`.

### 3. Multiple registrations

For now, simplest behavior:

- If multiple `registerEntryPoint` calls exist:
  - Generator still emits **one** entry point.
  - Runtime behavior: last call to `registerEntryPoint` wins.
- Optionally, generator can emit a diagnostic warning if more than one call is detected.

---

## Usage example (what Claude should validate)

**Program.fs:**

```fsharp
module Program

open Serde.FS
open Serde.FS.STJ

[<Serde>]
type Person = { Name: string; Age: int }

let run argv =
    SerdeJson.useAsDefault()

    let person = { Name = "John"; Age = 30 }
    let json = Serde.Serialize person
    printfn "Serialized: %s" json

    let deserialized: Person = Serde.Deserialize json
    printfn "Deserialized: %A" deserialized
    0

SerdeApp.registerEntryPoint run
```

**fsproj:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Serde.FS.STJ" Version="x.y.z" />
  </ItemGroup>
</Project>
```

Expected behavior:

- Build succeeds.
- Generator emits `Serde.Generated.EntryPoint` with `[<EntryPoint>]`.
- `dotnet run` executes `run` via `SerdeApp.invokeRegisteredEntryPoint`.

---

## Non‑goals / guardrails

- Do **not** try to:
  - Detect or replace existing `[<EntryPoint>]`.
  - Support multiple entry points.
  - Infer entry points without explicit `registerEntryPoint`.
  - Modify project type (Exe vs Library).
- If no `registerEntryPoint` is found:
  - Generator emits **no** entry point.
  - Behavior is unchanged from today.

---
