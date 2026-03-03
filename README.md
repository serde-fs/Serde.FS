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

Every serialized type must explicitly opt in using the `[<Serde>]` attribute. Serde.FS generates metadata at design time and uses a fast, predictable backend at runtime.

Serde.FS.Json is the first backend, providing a high‑performance JSON serializer built on top of System.Text.Json — but without reflection or runtime inference.

---

## ✨ Key Features

- **Strict, Serde‑style serialization**  
  Only types annotated with `[<Serde>]` participate. No fallback, no reflection.

- **Compile‑time validation**  
  Nested types must also have Serde metadata. Violations fail at build time.

- **Deterministic code generation**  
  The generator emits stable, predictable serializers/deserializers.

- **Fast runtime backend**  
  Serde.FS.Json uses generated code — not reflection — for maximum performance.

- **Custom converters**  
  Override serialization for specific types using attribute‑level converters.

- **Backend‑agnostic design**  
  JSON today, other formats tomorrow.

---

## 🚀 Getting Started

### 1. Install packages

```bash
dotnet add package Serde.FS.Json
```

### 2. Annotate your types

```fsharp
open Serde.FS

[<Serde>]
type Address = {
    Street : string
    City   : string
    Zip    : string
}

[<Serde>]
type Person = {
    Name    : string
    Age     : int
    Address : Address option
}
```

### 3. Use the JSON backend

```fsharp
open Serde.FS.Json

SerdeJson.useAsDefault()

let json = Serde.Serialize { Name = "Jordan"; Age = 30; Address = None }
let person : Person = Serde.Deserialize json
```

---

## 🔒 Strictness Model

Serde.FS enforces strictness at two levels:

### **1. Root strictness (runtime)**
If you try to serialize a type without `[<Serde>]`, the backend throws a strict violation.

### **2. Nested strictness (build‑time)**
If a Serde‑annotated type contains a nested type that lacks `[<Serde>]`, the generator fails at build time.

This mirrors Rust Serde’s behavior:  
> If a nested type doesn’t derive Serialize, the parent type cannot derive Serialize.

---

## 🧩 Custom Converters

Serde.FS supports attribute‑level converters that let you override serialization for specific types without weakening strictness.

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

Converters:

- are explicit  
- are compile‑time validated  
- do not bypass strictness  
- do not introduce fallback  
- can internally cache expensive state (e.g., STJ options)  

---

## 📦 SampleApp Example

The SampleApp includes a working converter example:

```fsharp
[<Serde(Custom = typeof<UppercaseNameConverter>)>]
type FancyName = { Value : string }

type UppercaseNameConverter() =
    interface ISerdeConverter<FancyName> with
        member _.Serialize(n) =
            JsonValue.String(n.Value.ToUpperInvariant())

        member _.Deserialize(node) =
            let s = node.AsString()
            { Value = s.ToLowerInvariant() }
```

Used inside a larger Serde‑annotated type:

```fsharp
[<Serde>]
type Person = {
    Name  : string
    Fancy : FancyName
}
```

---

## 🧠 Design Philosophy

Serde.FS is built on four principles:

- **Explicitness**  
  Only annotated types participate.

- **Determinism**  
  No runtime inference or fallback.

- **Compile‑time validation**  
  Errors surface early and predictably.

- **Backend independence**  
  Metadata is backend‑agnostic; backends are pluggable.

This makes Serde.FS ideal for:

- stable domain models  
- configuration files  
- deterministic logs  
- interop formats  
- systems where correctness matters more than flexibility  

It is *not* designed for:

- dynamic JSON  
- schema‑drifting storage  
- partial deserialization  
- runtime‑mutable behavior  

For those scenarios, System.Text.Json or Newtonsoft.Json are better fits.

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
