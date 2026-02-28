## Goal

Reshape the Serde.FS + SourceDjinn integration so that:

- **FSharp.SourceDjinn** owns *all* FCS/AST parsing and type discovery.
- **FSharp.SourceDjinn.TypeModel** is the shared, dependency-free type model.
- **Serde.FS.SourceGen**:
  - does **not** reference FCS,
  - does **not** do syntax walking,
  - only:
    - calls SourceDjinn APIs,
    - filters by attribute names,
    - builds Serde-specific metadata,
    - calls its emitter.

---

## Current state (what Claude will see)

In `Serde.FS.SourceGen`:

- `SerdeAstParser`:
  - opens `FSharp.Compiler.*`
  - uses `FSharpChecker`
  - walks `SynExpr`, `SynModuleDecl`, etc.
  - calls `TypeKindExtractor.extractTypes`
  - filters by Serde attributes
  - builds `SerdeTypeInfo`
  - detects `SerdeApp.entryPoint`

This mixes **engine responsibilities** and **Serde backend responsibilities**.

---

## Target architecture

### 1. Engine responsibilities (move into FSharp.SourceDjinn)

Claude should:

1. **Create engine APIs in FSharp.SourceDjinn** (or a new `FSharp.SourceDjinn.Api` module) that provide:

   - **File parsing to TypeInfo**  
     - `parseSource : filePath:string -> sourceText:string -> TypeInfo list`  
     - `parseFile : filePath:string -> TypeInfo list`

   - **Attribute-based filtering**  
     - `filterByAttributes : attributeNames:Set<string> -> TypeInfo list -> TypeInfo list`  
       - Uses `TypeInfo.Attributes` and short-name matching.

   - **Entry-point detection**  
     - `hasEntryPointRegistration : filePath:string -> sourceText:string -> bool`  
       - Move logic from:
         - `isEntryPointIdent`
         - `exprContainsEntryPointRegistration`
         - `declsContainEntryPointRegistration`
         - `hasEntryPointRegistration`

2. **Ensure all FCS usage lives in SourceDjinn**  
   - `FSharpChecker`, `SynExpr`, `SynModuleDecl`, `ParsedInput`, etc.  
   - No FCS references should remain in `Serde.FS.SourceGen`.

3. **Keep TypeModel as the only shared contract**  
   - All public APIs return/consume `TypeInfo` and related types from `FSharp.SourceDjinn.TypeModel`.

---

### 2. Backend responsibilities (simplify Serde.FS.SourceGen)

Claude should refactor `Serde.FS.SourceGen` so that:

1. **Remove all FCS references**  
   - Delete `open FSharp.Compiler.CodeAnalysis`
   - Delete `open FSharp.Compiler.Syntax`
   - Delete `open FSharp.Compiler.Text`
   - Remove `FSharpChecker.Create()` usage.

2. **Rewrite `SerdeAstParser` to use SourceDjinn APIs only**

   Replace current parsing logic with something like:

   ```fsharp
   module SerdeAstParser =

       let private serdeAttributeNames =
           set [
               "Serde"; "SerdeAttribute"
               "SerdeSerialize"; "SerdeSerializeAttribute"
               "SerdeDeserialize"; "SerdeDeserializeAttribute"
           ]

       let parseSource (filePath: string) (sourceText: string) : SerdeTypeInfo list =
           SourceDjinn.Api.parseSource filePath sourceText
           |> SourceDjinn.Api.filterByAttributes serdeAttributeNames
           |> List.map SerdeMetadataBuilder.buildSerdeTypeInfo

       let parseFile (filePath: string) : SerdeTypeInfo list =
           let sourceText = System.IO.File.ReadAllText(filePath)
           parseSource filePath sourceText

       let parseFileAllTypes (filePath: string) : TypeInfo list =
           let sourceText = System.IO.File.ReadAllText(filePath)
           SourceDjinn.Api.parseSource filePath sourceText

       let hasEntryPointRegistration (filePath: string) (sourceText: string) : bool =
           SourceDjinn.Api.hasEntryPointRegistration filePath sourceText

       let hasEntryPointRegistrationInFile (filePath: string) : bool =
           let sourceText = System.IO.File.ReadAllText(filePath)
           hasEntryPointRegistration filePath sourceText
   ```

3. **Keep Serde-specific logic only**  
   - Attribute name set (`serdeAttributeNames`)
   - `SerdeMetadataBuilder.buildSerdeTypeInfo`
   - Serde emitter usage.

---

### 3. Dependency expectations

Claude should ensure:

- **FSharp.SourceDjinn.TypeModel**
  - No dependencies.
  - Contains only type model definitions.

- **FSharp.SourceDjinn**
  - Depends on:
    - `FSharp.SourceDjinn.TypeModel`
    - `FSharp.Compiler.Service`
  - Exposes the new APIs described above.

- **Serde.FS.SourceGen**
  - Depends on:
    - `FSharp.SourceDjinn.TypeModel`
    - `FSharp.SourceDjinn`
    - `Serde.FS`
  - Does **not** reference FCS directly.

- **Serde.FS**
  - Depends on:
    - `FSharp.SourceDjinn.TypeModel`
  - Does **not** depend on:
    - `FSharp.SourceDjinn`
    - `Serde.FS.SourceGen`
    - FCS.

---

### 4. Acceptance criteria

Claude is “done” when:

1. `Serde.FS.SourceGen` builds and:
   - has **no** `open FSharp.Compiler.*` lines,
   - has **no** `FSharpChecker` usage,
   - has **no** direct `SynExpr`/`SynModuleDecl`/`ParsedInput` pattern matching.

2. `FSharp.SourceDjinn`:
   - exposes the parsing + filtering + entry-point APIs,
   - is the **only** project that references FCS.

3. The Serde generator still works end-to-end:
   - finds Serde-annotated types,
   - builds `SerdeTypeInfo`,
   - emits code,
   - detects `SerdeApp.entryPoint`.

---
