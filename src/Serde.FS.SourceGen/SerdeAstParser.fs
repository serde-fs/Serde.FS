namespace Serde.FS.SourceGen

open Serde.FS
open FSharp.SourceDjinn.TypeModel
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

    /// Check if source text contains a call to SerdeApp.entryPoint.
    let hasEntryPointRegistration (filePath: string) (sourceText: string) : bool =
        AstParser.hasEntryPointRegistration filePath sourceText

    /// Check if a source file contains a call to SerdeApp.entryPoint.
    let hasEntryPointRegistrationInFile (filePath: string) : bool =
        AstParser.hasEntryPointRegistrationInFile filePath
