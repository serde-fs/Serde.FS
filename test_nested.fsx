#load "src/Serde.FS/TypeKind.fs"
#load "src/FSharp.SourceDjinn/TypeKindExtractor.fs"
#load "src/Serde.FS/SerdeMetadata.fs"
#load "src/FSharp.SourceDjinn/AstParser.fs"

open Serde.FS
open Serde.FS.TypeKindTypes
open FSharp.SourceDjinn

let source = """
namespace MyApp

[<Serde>]
type Address = { Street: string; City: string }

[<Serde>]
type Person = { Name: string; Age: int; Address: Address; LuckyNumbers: Option<int Set> }
"""

let types = AstParser.parseSource "/test.fs" source

for t in types do
    printfn "Type: %s" t.Raw.TypeName
    printfn "  Namespace: %A" t.Raw.Namespace
    printfn "  EnclosingModules: %A" t.Raw.EnclosingModules
    printfn ""
    
    match t.Fields with
    | Some fields ->
        for f in fields do
            printfn "  Field: %s" f.Name
            printfn "    Type.TypeName: %s" f.Type.TypeName
            printfn "    Type.Namespace: %A" f.Type.Namespace
            printfn "    Type.EnclosingModules: %A" f.Type.EnclosingModules
            printfn "    Type.Kind: %A" f.Type.Kind
            printfn ""
    | None -> ()
