namespace Serde.FS

open System

/// Specifies a codec type to use for serialization and deserialization.
/// Can be applied at the type level or property level.
[<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct ||| AttributeTargets.Property ||| AttributeTargets.Field, AllowMultiple = false)>]
type JsonCodecAttribute(
    [<System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)>]
    codecType: Type) =
    inherit Attribute()
    /// A codec type with a public parameterless constructor; instantiated by the
    /// runtime resolver via Activator.CreateInstance (kept for trimming / AOT).
    [<field: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)>]
    [<property: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)>]
    member val CodecType : Type = codecType with get
