namespace Serde.FS.SourceGen

type SerdeCapability =
    | Serialize
    | Deserialize
    | Both

type FieldInfo = {
    Name: string
    FSharpType: string
}

type SerdeTypeInfo = {
    Namespace: string
    TypeName: string
    Capability: SerdeCapability
    Fields: FieldInfo list
}
