# **Serde.FS — Strict, Deterministic Serialization for F#**

[![Serde.FS](https://img.shields.io/nuget/vpre/Serde.FS.svg?label=Serde.FS)](https://www.nuget.org/packages/Serde.FS/)
[![Serde.FS.SourceGen](https://img.shields.io/nuget/vpre/Serde.FS.SourceGen.svg?label=Serde.FS.SourceGen)](https://www.nuget.org/packages/Serde.FS.SourceGen/)
[![Serde.FS.Json](https://img.shields.io/nuget/vpre/Serde.FS.Json.svg?label=Serde.FS.Json)](https://www.nuget.org/packages/Serde.FS.Json/)

Serde.FS is a strict, deterministic, compile‑time–validated serialization framework for F#. It brings the core ideas of Rust Serde into the .NET ecosystem:

- **No reflection**  
- **No runtime fallback**  
- **No silent behavior**  
- **No schema drift**  
- **No surprises**  

Every serialized type must explicitly opt in using `[<Serde>]`. Metadata is generated at design time, and backends use fast, predictable code at runtime.

Serde.FS.Json is the first backend, built on System.Text.Json — but without reflection or runtime inference.

Serde.FS.Json is powered by [FSharp.SourceDjinn](https://github.com/fs-djinn/FSharp.SourceDjinn), a lightweight source generator engine.

---

## 🚀 Quick Start (the whole system in 20 seconds)

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

SerdeJson.useAsDefault()

let json = Serde.Serialize { Name = "Jordan"; Age = 30 }
let person : Person = Serde.Deserialize json
```

That’s the entire workflow: **opt in → generate → serialize**.

---

## ✨ Key Features

- **Strict, Serde‑style serialization** — Only `[<Serde>]` types participate.  
- **Compile‑time validation** — Nested types must also be annotated.  
- **Deterministic code generation** — Stable, predictable serializers.  
- **Fast runtime backend** — F# Source Generated code, no reflection.  
- **Custom converters** — Override behavior per‑type without weakening strictness.  
- **Backend‑agnostic design** — JSON today, TOML/YAML tomorrow.

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

Serde.FS supports attribute‑level converters that override serialization for specific types.

### 1. Implement a converter

```fsharp
open System.Text.Json.Nodes
open Serde.FS

type UppercaseNameConverter() =
    interface ISerdeConverter<FancyName> with
        member _.Serialize(n) =
            JsonValue.String(n.Value.ToUpperInvariant())

        member _.Deserialize(node) =
            let s = node.AsString()
            { Value = s.ToLowerInvariant() }
```

### 2. Attach it to a type

```fsharp
[<Serde(Converter = typeof<UppercaseNameConverter>)>]
type FancyName = { Value : string }
```

### 3. Use it normally

```fsharp
let fancy = { Value = "Jordan" }
let json = Serde.Serialize fancy
// => "JORDAN"

let back : FancyName = Serde.Deserialize json
// => { Value = "jordan" }
```

Converters are explicit, compile‑time validated, and do not introduce fallback or reflection.

---

## 🧠 Mental Model

Serde.FS is not a general‑purpose .NET serializer. It is a **compile‑time, explicit, deterministic system** inspired by Rust Serde.

- Only annotated types participate.  
- No runtime inference or fallback.  
- Errors surface early and predictably.  
- Backends follow Serde semantics, not their own.  

Ideal for stable domain models, configuration files, deterministic logs, and interop formats.

Not designed for dynamic JSON, schema‑drifting storage, partial deserialization, or runtime‑mutable behavior.

---

### 🏁 Custom EntryPoint for CLI apps  
F# requires the real `[<EntryPoint>]` function to appear last in compilation order.  
Because Serde.FS uses source generation, it cannot safely generate the real entry point directly.  
 
To solve this, mark your intended entry point with:  
 
```fsharp
[<FSharp.SourceDjinn.TypeModel.EntryPoint>]
let main argv = ...
```  
 
SourceDjinn will generate the actual `[<EntryPoint>]` wrapper in a separate file so it appears in the correct place in the compilation order.

---


## 🧩 How It Works

```
F# source
   ↓
[<Serde>] attributes
   ↓
FSharp.SourceDjinn extracts metadata
   ↓
Serde.FS.SourceGen validates + generates serializers
   ↓
Serde.FS.Json uses generated code at runtime
```

---

## 🧱 Design Philosophy

- **Explicitness** — Only annotated types participate.  
- **Determinism** — No runtime inference or fallback.  
- **Compile‑time validation** — Errors surface early.  
- **Backend independence** — Metadata is backend‑agnostic.

---

## 📚 Roadmap

- Additional backends (TOML, YAML)  
- Field‑level overrides (`rename`, `skip`, `flatten`)  
- Improved diagnostics  
- Optional compile‑time schema generation  

---

## ❤️ Acknowledgements

Serde.FS is inspired by the elegance and rigor of Rust Serde, adapted to the F# ecosystem with a focus on clarity, determinism, and developer experience.

---
