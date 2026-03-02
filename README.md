# **Serde.FS**

Reflection‑free, compile‑time serialization for F#. Serde.FS brings the Serde philosophy to .NET with a shared type‑shape core and pluggable backends such as System.Text.Json.

## Status

Early development. APIs and package structure are still evolving.

## Projects

- **Serde.FS** — core abstractions, attributes, and type‑shape model
- **Serde.FS.Json** — System.Text.Json backend powered by source generation

## Goals

- Reflection‑free, deterministic serialization
- NativeAOT and WASM compatibility
- First‑class support for F# records, DUs, and options
- Extensible backend model (JSON, TOML, YAML, etc.)

## License

MIT
