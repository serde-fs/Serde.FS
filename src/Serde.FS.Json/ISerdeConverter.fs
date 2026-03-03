namespace Serde.FS.Json

open System.Text.Json.Nodes

type ISerdeConverter<'T> =
    abstract Serialize : 'T -> JsonNode
    abstract Deserialize : JsonNode -> 'T
