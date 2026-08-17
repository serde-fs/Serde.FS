namespace Serde.FS.SourceGen

open Serde.FS
open FSharp.SourceDjinn.TypeModel.Types
open FSharp.SourceDjinn

module SerdeAstParser =

    let private serdeAttributeNames =
        set [
            "Serde"; "SerdeAttribute"
            "SerdeSerialize"; "SerdeSerializeAttribute"
            "SerdeDeserialize"; "SerdeDeserializeAttribute"
        ]

    /// Parse F# source text and return all Serde-annotated types found.
    let parseSource (filePath: string) (sourceText: string) : SerdeTypeInfo list =
        AstParser.parseSourceAllTypes filePath sourceText
        |> AstParser.filterByAttributes serdeAttributeNames
        |> List.map SerdeMetadataBuilder.buildSerdeTypeInfo

    /// Parse an F# source file and return all Serde-annotated types found.
    let parseFile (filePath: string) : SerdeTypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        parseSource filePath sourceText

    /// Parse an F# source file and return ALL type definitions (for building lookup maps).
    let parseFileAllTypes (filePath: string) : TypeInfo list =
        AstParser.parseFileAllTypes filePath

    /// Parse F# source text and return ALL type definitions (for building lookup maps).
    let parseSourceAllTypes (filePath: string) (sourceText: string) : TypeInfo list =
        AstParser.parseSourceAllTypes filePath sourceText

    /// Root-level calls whose explicit type argument names a type that is
    /// serialized directly (not via a field): `Serde.Serialize<T>` /
    /// `Serde.Deserialize<T>` on the backend-agnostic facade, and the JSON
    /// backend's `SerdeJson.serialize<T>` family. Type arguments found here feed
    /// generic / option / tuple / collection discovery, so e.g. a root
    /// `SerdeJson.serialize<Person list>` gets a statically-typed codec and does not
    /// fall back to the reflection factories under Native AOT (issue #14).
    let private serdeCallNames =
        set [
            "Serde.Serialize"; "Serde.Deserialize"
            "SerdeJson.serialize"; "SerdeJson.deserialize"
            "SerdeJson.serializeToUtf8"; "SerdeJson.deserializeFromUtf8"
        ]

    /// Extract type arguments from explicit Serde.Serialize<T>/Serde.Deserialize<T> calls.
    let parseFileRootTypeArgs (filePath: string) : TypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        AstParser.extractCallTypeArgs serdeCallNames filePath sourceText

    /// Extract type arguments from explicit Serde.Serialize<T>/Serde.Deserialize<T> calls (from source text).
    let parseSourceRootTypeArgs (filePath: string) (sourceText: string) : TypeInfo list =
        AstParser.extractCallTypeArgs serdeCallNames filePath sourceText

