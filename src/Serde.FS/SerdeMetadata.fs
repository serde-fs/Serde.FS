namespace Serde.FS

type SerdeCapability =
    | Serialize
    | Deserialize
    | Both

type FieldInfo = {
    Name: string
    FSharpType: string
}

type SerdeTypeInfo = {
    Namespace: string option
    EnclosingModules: string list
    TypeName: string
    Capability: SerdeCapability
    Fields: FieldInfo list
}
