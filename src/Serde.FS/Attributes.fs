namespace Serde.FS

open System

/// Marks a type for both serialization and deserialization code generation.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum, AllowMultiple = false)>]
type SerdeAttribute() =
    inherit Attribute()
    member val Codec : Type = null with get, set

/// Marks a type for serialization code generation only.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum, AllowMultiple = false)>]
type SerdeSerializeAttribute() = inherit Attribute()

/// Marks a type for deserialization code generation only.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum, AllowMultiple = false)>]
type SerdeDeserializeAttribute() = inherit Attribute()

/// Renames the target element in serialized output.
[<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
type SerdeRenameAttribute(name: string) =
    inherit Attribute()
    member _.Name = name

/// Skips the target element for both serialization and deserialization.
[<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
type SerdeSkipAttribute() = inherit Attribute()

/// Skips the target element for serialization only.
[<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
type SerdeSkipSerializeAttribute() = inherit Attribute()

/// Skips the target element for deserialization only.
[<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
type SerdeSkipDeserializeAttribute() = inherit Attribute()

/// Specifies field-level codec overrides for serialization and deserialization.
[<AttributeUsage(AttributeTargets.Field ||| AttributeTargets.Property, AllowMultiple = false)>]
type SerdeFieldAttribute() =
    inherit Attribute()
    member val Codec : Type = null with get, set

/// Marks a function as the application entry point for source-generated bootstrapping.
[<AttributeUsage(AttributeTargets.Method, AllowMultiple = false)>]
type EntryPointAttribute() = inherit Attribute()

/// Controls how RPC method names are transformed into URL segments.
type UrlCase =
    /// Use method name exactly as declared (no transformation).
    | Default = 0
    /// Convert to kebab-case (e.g., GetProducts -> get-products).
    | Kebab = 1

/// Marks an interface as an RPC API contract. The source generator will walk
/// all abstract member signatures and generate codecs for every type in the
/// transitive closure, without requiring [<Serde>] on those types.
/// Optional Root and Version control the route prefix: /rpc/{Root}/{Version}/{Method}
[<AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)>]
type RpcApiAttribute() =
    inherit Attribute()
    /// Custom route namespace. Defaults to the interface name.
    member val Root : string = null with get, set
    /// Version segment (e.g., "v2"). Omitted if null.
    member val Version : string = null with get, set
    /// Controls how method names are transformed into URL segments.
    member val UrlCase : UrlCase = UrlCase.Default with get, set

/// Applied alongside [<RpcApi>] on an interface to request generation of a
/// Fable-compatible client proxy + JSON codecs. The generator writes the file
/// to the project containing the interface under generated-fable/{InterfaceName}.fs
/// by default. The emitted code uses Fable.Core types, which compile under both
/// .NET and Fable: Fable produces the real browser-side client, while .NET treats
/// the module as dead code that would throw at runtime if invoked.
[<AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)>]
type GenerateFableClientAttribute() =
    inherit Attribute()
    /// Optional override for the output directory. Relative paths are resolved
    /// against the directory containing the annotated interface's source file.
    /// When null, defaults to "generated-fable" under that project.
    member val OutputDir : string = null with get, set

/// Defines a discoverable bootstrap that will be automatically run during the entry point startup sequence.
type IEntryPointBootstrap =
    abstract member Init : unit -> unit
