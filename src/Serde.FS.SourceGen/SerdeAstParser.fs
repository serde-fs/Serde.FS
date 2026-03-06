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

    let private serdeCallNames = set [ "Serde.Serialize"; "Serde.Deserialize" ]

    /// Extract type arguments from explicit Serde.Serialize<T>/Serde.Deserialize<T> calls.
    let parseFileRootTypeArgs (filePath: string) : TypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        AstParser.extractCallTypeArgs serdeCallNames filePath sourceText

    /// Extract type arguments from explicit Serde.Serialize<T>/Serde.Deserialize<T> calls (from source text).
    let parseSourceRootTypeArgs (filePath: string) (sourceText: string) : TypeInfo list =
        AstParser.extractCallTypeArgs serdeCallNames filePath sourceText

