### Title  
FSharp.SourceDjinn – Bootstrap conventions and EntryPoint behavior

---

### Scope  
Implement Djinn’s startup behavior and the conventions that SourceGen packages (frameworks) use to activate their generated metadata.

This spec covers:

- The Djinn‑generated EntryPoint behavior
- The reflection‑based convention bootstrap
- The Djinn‑owned fallback reflection bootstrap
- The framework‑owned explicit AOT bootstrap
- The namespace conventions that encode ownership and intent

---

### 1. Djinn EntryPoint behavior

Djinn generates an `EntryPoint` that performs three steps in order:

```fsharp
[<EntryPoint>]
let main argv =
    Bootstrap.tryConventionBootstrap ()
    Bootstrap.fallbackToReflectionBootstrap ()
    Program.run argv
```

Where `Bootstrap` is a Djinn‑generated internal module (name flexible, but Djinn‑owned).

#### 1.1 `tryConventionBootstrap`

- **Goal:** Let frameworks plug into Djinn’s startup lifecycle via a simple convention.
- **Behavior:**
  - Get the entry assembly.
  - Look for a type named `Djinn.Generated.Bootstrap` (namespace `Djinn.Generated`).
  - Look for a public static method `init : unit -> unit`.
  - If found, invoke it once.
  - If not found, do nothing.
- **Constraints:**
  - No sweeping or scanning beyond this single type lookup.
  - No exceptions should bubble out; failures should be swallowed or logged in a debug‑friendly way.
  - This function must not perform any other reflection or registration.

Pseudocode:

```fsharp
module internal Bootstrap =

    let mutable private conventionBootstrapWasCalled = false

    let tryConventionBootstrap () =
        let asm = System.Reflection.Assembly.GetEntryAssembly()
        if not (isNull asm) then
            match asm.GetType("Djinn.Generated.Bootstrap") with
            | null -> ()
            | ty ->
                let m =
                    ty.GetMethod(
                        "init",
                        System.Reflection.BindingFlags.Public
                        ||| System.Reflection.BindingFlags.Static
                    )
                if not (isNull m) && m.GetParameters().Length = 0 then
                    m.Invoke(null, [||]) |> ignore
                    conventionBootstrapWasCalled <- true
```

> **Note:** `conventionBootstrapWasCalled` is used by the fallback.

---

#### 1.2 `fallbackToReflectionBootstrap`

- **Goal:** Ensure generated metadata is activated even when no framework bootstrap is provided.
- **Precondition:** Only runs meaningful logic if `conventionBootstrapWasCalled = false`.
- **Behavior (high‑level):**
  - If `conventionBootstrapWasCalled` is `true`, return immediately.
  - Otherwise, perform minimal, targeted reflection to ensure Djinn’s own generated metadata is activated.
  - This might mean:
    - Forcing module initializers to run, or
    - Touching known Djinn‑generated types to trigger static constructors, or
    - Any other minimal mechanism you choose to “wake up” generated registrations.
- **Constraints:**
  - No broad assembly scanning.
  - No attribute sweeps.
  - No framework‑specific knowledge.
  - Only touches Djinn‑owned / Djinn‑generated types.
  - Safe to run even if nothing needs activation.

Pseudocode skeleton:

```fsharp
    let fallbackToReflectionBootstrap () =
        if conventionBootstrapWasCalled then
            ()
        else
            // Minimal, Djinn-owned reflection to ensure generated metadata is activated.
            // Example: touch a known Djinn-generated registry type, or force-load a module.
            ()
```

The exact mechanism can be refined later; for now, Claude should keep it minimal and Djinn‑scoped.

---

### 2. Framework conventions

Framework authors building SourceGen packages on top of FSharp.SourceDjinn have **two** bootstrap strategies.

#### 2.1 Magic path – convention bootstrap (reflection, zero boilerplate)

- **Framework responsibility:**
  - Generate a module with this exact shape:

    ```fsharp
    namespace Djinn.Generated

    module Bootstrap =
        let init () =
            // Framework-specific registration / activation of generated metadata.
            ()
    ```

- **Djinn behavior:**
  - `tryConventionBootstrap` will find and invoke `Djinn.Generated.Bootstrap.init()`.
  - `fallbackToReflectionBootstrap` will see `conventionBootstrapWasCalled = true` and do nothing.

- **Intention:**
  - “Framework wants Djinn to auto‑bootstrap using reflection.”
  - This is the “it just works” path.

- **Notes:**
  - Framework authors **do not** write any reflection.
  - They only generate pure F# code inside `init()`.

---

#### 2.2 AOT path – explicit bootstrap (no reflection, full control)

- **Framework responsibility:**
  - Generate a module with a framework‑owned namespace, e.g.:

    ```fsharp
    namespace MyFramework.Generated

    module DjinnBootstrap =
        let init () =
            // Framework-specific registration / activation of generated metadata.
            ()
    ```

  - Call it explicitly from the host app or framework entry:

    ```fsharp
    MyFramework.Generated.DjinnBootstrap.init()
    ```

- **Djinn behavior:**
  - `tryConventionBootstrap` will **not** find `Djinn.Generated.Bootstrap`.
  - `fallbackToReflectionBootstrap` may run, but should be minimal and not interfere with framework behavior.
  - Djinn never looks for or calls `MyFramework.Generated.DjinnBootstrap`.

- **Intention:**
  - “Framework wants full determinism and AOT‑friendliness.”
  - This is the explicit, reflection‑free path.

- **Rules:**
  - In AOT mode, frameworks **must not** generate `Djinn.Generated.Bootstrap`.
  - That namespace is reserved for the magic path.

---

### 3. Namespace semantics

The first segment of the namespace encodes ownership and intent:

- `Djinn.Generated.*`
  - Djinn‑defined conventions.
  - Djinn is allowed to discover and call these via reflection.
  - Used only for the magic path.

- `*.Generated.*` (e.g., `MyFramework.Generated.*`)
  - Framework‑owned generated code.
  - Djinn must not assume anything about these.
  - Used for explicit AOT bootstraps and other framework internals.

Claude should treat this as a hard contract.

---

### 4. Non‑goals / explicit constraints

- Djinn must not:
  - Depend on any specific framework.
  - Scan all types for attributes.
  - Mutate or generate framework‑owned types.
  - Require frameworks to participate in reflection.
- Frameworks must not:
  - Generate `Djinn.Generated.Bootstrap` for AOT scenarios.
  - Rely on Djinn calling any framework‑specific bootstrap automatically.

---
