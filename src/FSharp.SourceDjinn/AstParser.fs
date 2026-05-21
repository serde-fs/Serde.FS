namespace FSharp.SourceDjinn

open FSharp.SourceDjinn.TypeModel.Types

module AstParser =

    // ── Attribute filtering ──────────────────────────────────────────

    let private shortName (name: string) =
        match name.LastIndexOf('.') with
        | -1 -> name
        | i -> name.Substring(i + 1)

    /// Filter types to only those having at least one attribute whose short name is in the given set.
    let filterByAttributes (attrNames: Set<string>) (types: TypeInfo list) : TypeInfo list =
        types
        |> List.filter (fun ti ->
            ti.Attributes |> List.exists (fun a ->
                attrNames.Contains(shortName a.Name)))

    // ── Type parsing (delegates to TypeKindExtractor) ────────────────

    /// Parse F# source text and return ALL type definitions found.
    let parseSourceAllTypes (filePath: string) (sourceText: string) : TypeInfo list =
        TypeKindExtractor.extractTypes filePath sourceText

    /// Parse an F# source file and return ALL type definitions found.
    let parseFileAllTypes (filePath: string) : TypeInfo list =
        let sourceText = System.IO.File.ReadAllText(filePath)
        TypeKindExtractor.extractTypes filePath sourceText

    // ── Call-site type argument extraction ─────────────────────────────

    /// Extract explicit type arguments from calls to the named functions in the given source.
    let extractCallTypeArgs (functionNames: Set<string>) (filePath: string) (sourceText: string) : TypeInfo list =
        CallTypeArgExtractor.extractCallTypeArgs functionNames filePath sourceText

