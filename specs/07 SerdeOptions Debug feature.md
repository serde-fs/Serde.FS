# ⭐ Claude Spec — Add Debug Mode to SerdeOptions + SerdeJson.enableDebug()

This spec introduces:

- A new `Debug` flag on `SerdeOptions`
- A `SerdeJson.enableDebug()` helper
- A `SerdeDebug` internal module for logging
- Debug hooks in generated converters
- Debug hooks in strict‑mode enforcement
- Debug hooks in resolver registration

The goal is to give users a clear, opt‑in way to verify that generated converters are being used, without affecting performance or behavior when debug mode is off.

---

# 1. Add `Debug` flag to `SerdeOptions`

Modify the core Serde options record:

```fsharp
type SerdeOptions =
    { mutable Strict : bool
      mutable Debug : bool }
```

Defaults:

```fsharp
Strict = true
Debug = false
```

Debug must be **off by default**.

---

# 2. Add `enableDebug()` to `SerdeJson`

Expose a simple, intention‑revealing API:

```fsharp
module SerdeJson =
    let enableDebug () =
        Serde.Options.Debug <- true
```

No `disableDebug()` is needed because debug is off by default and rarely toggled.

---

# 3. Add internal `SerdeDebug` module

This module centralizes debug logging and ensures zero overhead when disabled.

```fsharp
module internal SerdeDebug =
    let log (msg : string) =
        if Serde.Options.Debug then
            printfn "[SerdeDebug] %s" msg
```

Requirements:

- Must be internal
- Must check the Debug flag before printing
- Must incur zero overhead when Debug = false

---

# 4. Add debug hooks to generated converters

Every generated converter should emit a debug message when used.

Example (inside generated `Read`/`Write` methods):

```fsharp
if Serde.Options.Debug then
    SerdeDebug.log $"Using generated converter for {typeof<'T>.FullName}"
```

This must be:

- behind the Debug flag  
- zero‑overhead when disabled  
- consistent across all generated converters  

---

# 5. Add debug hooks to strict‑mode enforcement

When strict mode checks metadata:

```fsharp
if Serde.Options.Debug then
    if typeInfo <> null then
        SerdeDebug.log $"Strict mode: found generated metadata for {ty.FullName}"
    else
        SerdeDebug.log $"Strict mode: NO metadata found for {ty.FullName}"
```

This gives users visibility into:

- whether metadata is attached  
- whether strict mode is working  
- why a type fails strict mode  

---

# 6. Add debug hook when attaching the generated resolver

When the backend registers the generated resolver:

```fsharp
if Serde.Options.Debug then
    SerdeDebug.log "Attaching generated STJ resolver to JsonSerializerOptions"
```

This helps diagnose:

- resolver registration issues  
- metadata not being picked up  
- incorrect project layout  

---

# 7. Acceptance criteria

### Debug mode OFF (default)
- No debug output is printed  
- No performance overhead  
- Serialization uses generated converters normally  
- Strict mode works normally  

### Debug mode ON
- Generated converters print debug messages  
- Strict mode prints metadata checks  
- Resolver registration prints a debug message  
- Users can clearly see that generated converters are being used  
- No reflection fallback occurs unless strict mode is disabled  

### AOT safety
- Debug logging must not use reflection over user types  
- Debug logging must not break trimming  
- Debug logging must not affect metadata resolution  

---

# ⭐ Summary

This spec gives Serde.FS:

- A clean, ergonomic debug mode  
- A consistent cross‑backend debugging convention  
- A way for users to *see* that generated converters are being used  
- Zero overhead when disabled  
- AOT‑safe, reflection‑free behavior  
- A simple, discoverable API (`SerdeJson.enableDebug()`)  
