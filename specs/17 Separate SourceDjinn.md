# FSharp.SourceDjinn Refactor Specification (High‑Level)

This spec describes how to refactor the existing code so that:

- **FSharp.SourceDjinn** becomes a standalone, reusable F# source‑generation engine.
- **Serde.FS.SourceGen** becomes the Serde‑specific backend.
- No Serde‑specific code remains inside the engine.
- The type‑model layer moves into the engine.
- The engine exposes a clean, stable API for future generators.

---

## 1. New Project Boundaries

### FSharp.SourceDjinn (engine)
This project must contain only **Serde‑agnostic** components:

- Type model (TypeKind, TypeInfo, FieldInfo, EnumCase, UnionCase, AttributeInfo)
- TypeKindExtractor (AST → TypeInfo)
- AstParser (engine‑only parsing)
- CodeEmitter DSL (indentation, blocks, builder)
- Generator infrastructure (if any)

It must **not** reference Serde.FS.

### Serde.FS.SourceGen (Serde backend)
This project contains all Serde‑specific logic:

- Serde attributes
- SerdeTypeInfo
- Serde metadata builder
- Serde code emitters
- Serde generator task
- SerdeApp.entryPoint detection
- Filtering for Serde‑annotated types

It references **FSharp.SourceDjinn**.

---

## 2. File‑by‑File Refactor Instructions

### A. TypeKindExtractor.fs (engine)
**Current:**  
Imports `Serde.FS.TypeKindTypes`.

**Refactor:**
- Move the entire type model (TypeKind, TypeInfo, FieldInfo, etc.) into a new folder:
  ```
  FSharp.SourceDjinn/TypeModel/
  ```
- Update imports to:
  ```fsharp
  open FSharp.SourceDjinn.TypeModel
  ```
- Ensure the extractor produces only engine‑level metadata (`TypeInfo`, `TypeKind`, etc.).
- Remove any Serde‑specific filtering or assumptions.

**Result:**  
Pure engine component.

---

### B. AstParser.fs (split into engine + Serde layers)

**Current:**  
Mixes engine parsing with Serde filtering and Serde metadata building.

**Refactor:**

#### Engine portion (stays in FSharp.SourceDjinn)
Keep:
- `parseFileAllTypes`
- `parseSourceAllTypes`
- AST traversal logic
- Syntax tree walking
- Utilities for extracting declarations and expressions

Remove:
- Serde attribute names
- Serde filtering
- Serde metadata building
- SerdeApp.entryPoint detection

Update imports to:
```fsharp
open FSharp.SourceDjinn.TypeModel
```

#### Serde portion (moves to Serde.FS.SourceGen)
Move:
- `serdeAttributeNames`
- `isSerdeAnnotated`
- `SerdeMetadataBuilder.buildSerdeTypeInfo`
- `parseFile` (Serde‑filtered)
- `parseSource` (Serde‑filtered)
- `hasEntryPointRegistration`
- `hasEntryPointRegistrationInFile`

**Result:**  
AstParser becomes a clean engine parser; Serde.FS.SourceGen owns Serde filtering.

---

### C. CodeEmitter.fs (remove from engine, replace with DSL)

**Current:**  
A Serde‑specific wrapper:

```fsharp
open Serde.FS
let emit (emitter: ISerdeCodeEmitter) (info: SerdeTypeInfo) = emitter.Emit(info)
```

**Refactor:**
- Delete this file from FSharp.SourceDjinn.
- Move any Serde‑specific emission logic into Serde.FS.SourceGen.
- Ensure the real engine DSL (indentation, blocks, builder) lives in:
  ```
  FSharp.SourceDjinn/CodeEmitter.fs
  ```
- The engine DSL must not reference Serde.

**Result:**  
Engine owns a reusable code‑emission DSL; Serde owns its emitters.

---

### D. SerdeGeneratorTask.fs (move entirely to Serde.FS.SourceGen)

**Current:**  
Imports Serde.FS, Serde.FS.TypeKindTypes, and contains Serde‑specific logic.

**Refactor:**
- Move the entire file into Serde.FS.SourceGen.
- Update imports to:
  ```fsharp
  open FSharp.SourceDjinn.TypeModel
  open FSharp.SourceDjinn.CodeEmitter
  ```
- Ensure it uses the engine’s TypeInfo and CodeEmitter DSL.
- Remove all Serde references from the engine.

**Result:**  
Serde.FS.SourceGen becomes the Serde backend; engine becomes clean.

---

## 3. Type Model Migration

Move the following from Serde.FS into FSharp.SourceDjinn:

- PrimitiveKind
- TypeKind
- TypeInfo
- FieldInfo
- EnumCase
- UnionCase
- AttributeInfo
- typeInfoToFSharpString
- typeInfoToPascalName
- typeInfoToFqFSharpType

Place them under:

```
FSharp.SourceDjinn/TypeModel/
```

Update namespaces accordingly.

Serde.FS should import:

```fsharp
open FSharp.SourceDjinn.TypeModel
```

---

## 4. New Engine API Surface

After refactor, FSharp.SourceDjinn exposes:

- `TypeModel` (TypeKind, TypeInfo, etc.)
- `TypeKindExtractor.extractTypes`
- `AstParser.parseFileAllTypes`
- `AstParser.parseSourceAllTypes`
- `CodeEmitter` DSL
- (Optional) generator registration infrastructure

No Serde imports.  
No SerdeTypeInfo.  
No SerdeGeneratorTask.  
No SerdeApp logic.

---

## 5. New Serde Backend API Surface

Serde.FS.SourceGen exposes:

- Serde attributes
- SerdeTypeInfo
- Serde metadata builder
- Serde code emitters
- Serde generator task
- SerdeApp.entryPoint detection
- Serde‑filtered parsing using engine’s TypeInfo

---

## 6. Build + Packaging Notes

- After refactor, FSharp.SourceDjinn becomes a standalone analyzer package.
- Serde.FS.Json references it via:
  ```xml
  <PackageReference Include="FSharp.SourceDjinn" Version="x.y.z" PrivateAssets="all" />
  ```
- No analyzer DLLs should be copied manually.
- No Serde DLLs should appear in the engine package.

---

## 7. Migration Steps (for Claude)

1. Move type model into FSharp.SourceDjinn.
2. Update TypeKindExtractor to import engine type model.
3. Split AstParser into engine + Serde layers.
4. Remove Serde imports from engine.
5. Move SerdeGeneratorTask into Serde.FS.SourceGen.
6. Replace CodeEmitter.fs with engine DSL; move Serde wrapper into Serde.FS.SourceGen.
7. Update namespaces and references.
8. Ensure engine builds without Serde.FS.
9. Update Serde.FS.SourceGen to consume engine APIs.
10. Update STJ backend to reference engine package.

---
