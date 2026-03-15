namespace Serde.FS

open System

/// Marks a type for both serialization and deserialization code generation.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Interface ||| AttributeTargets.Enum, AllowMultiple = false)>]
type SerdeAttribute() =
    inherit Attribute()
    member val Converter : obj = null with get, set
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
