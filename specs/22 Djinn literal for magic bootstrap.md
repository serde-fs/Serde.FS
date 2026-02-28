### Title  
FSharp.SourceDjinn – Convention literal for magic bootstrap

---

### Scope  
Extend the existing Djinn bootstrap/EntryPoint implementation to introduce a **single, canonical literal** for the magic bootstrap convention:

- `Djinn.Generated.Bootstrap.init()`

This literal must be:

- Defined once in `FSharp.SourceDjinn`
- Used internally by Djinn’s EntryPoint generation
- Available for framework authors to use when generating their own magic‑path bootstrap

No changes to AOT/bootstrap behavior beyond wiring this literal through.

---

### 1. Public conventions module

Add a new public module to the main `FSharp.SourceDjinn` assembly:

```fsharp
namespace FSharp.SourceDjinn

module Conventions =
    [<Literal>]
    let ConventionBootstrapType = "Djinn.Generated.Bootstrap"

    [<Literal>]
    let ConventionBootstrapMethod = "init"
```

**Requirements:**

- This module is public and part of the stable API.
- It is the **only** place where the convention name is defined.
- It is intended for:
  - Djinn’s own internal use (EntryPoint generation)
  - Framework authors generating the magic bootstrap module

No additional literals for AOT or fallback behavior.

---

### 2. Update Djinn’s generated EntryPoint to use the literal

Current generated code (simplified):

```fsharp
namespace FSharp.SourceDjinn.Generated

module internal DjinnBootstrap =
    let mutable private conventionBootstrapWasCalled = false

    let tryConventionBootstrap () =
        try
            let asm = System.Reflection.Assembly.GetEntryAssembly()
            if not (isNull asm) then
                match asm.GetType("Djinn.Generated.Bootstrap") with
                | null -> ()
                | ty ->
                    let m =
                        ty.GetMethod(
                            "init",
                            System.Reflection.BindingFlags.Public
                            ||| System.Reflection.BindingFlags.Static)
                    if not (isNull m) && m.GetParameters().Length = 0 then
                        m.Invoke(null, [||]) |> ignore
                        conventionBootstrapWasCalled <- true
        with _ -> ()
```

**Change this to use the literal from `FSharp.SourceDjinn.Conventions`:**

```fsharp
open FSharp.SourceDjinn

namespace FSharp.SourceDjinn.Generated

module internal DjinnBootstrap =
    let mutable private conventionBootstrapWasCalled = false

    let tryConventionBootstrap () =
        try
            let asm = System.Reflection.Assembly.GetEntryAssembly()
            if not (isNull asm) then
                match asm.GetType(Conventions.ConventionBootstrapType) with
                | null -> ()
                | ty ->
                    let m =
                        ty.GetMethod(
                            Conventions.ConventionBootstrapMethod,
                            System.Reflection.BindingFlags.Public
                            ||| System.Reflection.BindingFlags.Static)
                    if not (isNull m) && m.GetParameters().Length = 0 then
                        m.Invoke(null, [||]) |> ignore
                        conventionBootstrapWasCalled <- true
        with _ -> ()
```

**Constraints:**

- Behavior must remain identical to current implementation.
- Only the hard‑coded strings are replaced with the literal.
- `fallbackToReflectionBootstrap` remains unchanged for now.

---

### 3. Framework author usage (documentation target)

This is not code to implement, but behavior to assume and support:

A SourceGen framework that wants the **magic path** will generate:

```fsharp
namespace Djinn.Generated

module Bootstrap =
    let init () =
        // Framework-specific registration / activation of generated metadata
        ()
```

Framework authors **may** reference the literal when generating this:

- `FSharp.SourceDjinn.Conventions.ConventionBootstrapType`
- `FSharp.SourceDjinn.Conventions.ConventionBootstrapMethod`

But Djinn does **not** enforce how they construct the file—only that the final compiled type/method matches the convention.

---

### 4. Non‑goals / explicit constraints

- Do **not** add any literals for:
  - AOT bootstrap names
  - Fallback reflection triggers
- Do **not** change:
  - `fallbackToReflectionBootstrap` behavior
  - AOT/explicit bootstrap behavior
- Do **not** move this literal into TypeModel or any shared metadata assembly.

The literal lives only in `FSharp.SourceDjinn.Conventions` and is reused internally by Djinn.