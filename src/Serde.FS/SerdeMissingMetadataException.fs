namespace Serde.FS

type SerdeMissingMetadataException(message: string, inferredType: System.Type) =
    inherit System.Exception(message)
    member _.InferredType = inferredType
