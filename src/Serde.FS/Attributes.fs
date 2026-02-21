namespace Serde.FS

open System

/// Marks a type for both serialization and deserialization code generation.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type SerdeAttribute() = inherit Attribute()

/// Marks a type for serialization code generation only.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type SerdeSerializeAttribute() = inherit Attribute()

/// Marks a type for deserialization code generation only.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type SerdeDeserializeAttribute() = inherit Attribute()
