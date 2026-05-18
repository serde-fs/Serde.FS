/// Builders for synthetic TypeInfo / SerdeTypeInfo / RpcInterfaceInfo /
/// RpcMethodInfo values used by snapshot tests. Lets us exercise
/// FableClientEmitter against varied type shapes without standing up a full
/// discovery pipeline for each test.
module Serde.FS.SourceGen.Tests.Fable.SyntheticTypes

open FSharp.SourceDjinn.TypeModel.Types
open Serde.FS

// ── Primitives ─────────────────────────────────────────────────────────────

let prim (kind: PrimitiveKind) (typeName: string) : TypeInfo =
    { Namespace = None
      EnclosingModules = []
      TypeName = typeName
      Kind = Primitive kind
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

let int32Ti   = prim Int32 "int"
let int64Ti   = prim Int64 "int64"
let stringTi  = prim PrimitiveKind.String "string"
let boolTi    = prim Bool "bool"
let decimalTi = prim PrimitiveKind.Decimal "decimal"
let floatTi   = prim Float64 "float"
let unitTi    = prim Unit "unit"
let guidTi    = prim Guid "Guid"

// ── Structural wrappers ────────────────────────────────────────────────────

let private synthetic (typeName: string) (kind: TypeKind) : TypeInfo =
    { Namespace = None
      EnclosingModules = []
      TypeName = typeName
      Kind = kind
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

let opt (inner: TypeInfo) : TypeInfo = synthetic "option" (Option inner)
let listTi (inner: TypeInfo) : TypeInfo = synthetic "list" (List inner)
/// `seq<T>` — wire shape identical to list, but FableClientEmitter decodes
/// to a seq (so consuming F# code expecting `seq<T>` accepts it).
let seqTi (inner: TypeInfo) : TypeInfo = synthetic "seq" (List inner)
let arrTi (inner: TypeInfo) : TypeInfo = synthetic "array" (Array inner)
let setTi (inner: TypeInfo) : TypeInfo = synthetic "Set" (Set inner)
let mapTi (k: TypeInfo) (v: TypeInfo) : TypeInfo = synthetic "Map" (Map (k, v))

let tupTi (elements: TypeInfo list) : TypeInfo =
    let fields =
        elements
        |> List.mapi (fun i ti ->
            { Name = sprintf "Item%d" (i + 1); Type = ti; Attributes = [] } : FieldInfo)
    synthetic "tuple" (Tuple fields)

let resultTi (okTi: TypeInfo) (errTi: TypeInfo) : TypeInfo =
    { Namespace = None
      EnclosingModules = []
      TypeName = "Result"
      Kind = ConstructedGenericType
      Attributes = []
      GenericParameters = []
      GenericArguments = [ okTi; errTi ] }

// ── User types ─────────────────────────────────────────────────────────────

let private mkField (name: string, ti: TypeInfo) : FieldInfo =
    { Name = name; Type = ti; Attributes = [] }

/// Build a record TypeInfo. `namespacePath` is the dotted namespace (e.g.
/// "Domain.Catalog"), `name` is the type name, `fields` is (name, type) pairs.
let record (namespacePath: string) (name: string) (fields: (string * TypeInfo) list) : TypeInfo =
    let nsOpt = if System.String.IsNullOrEmpty namespacePath then None else Some namespacePath
    { Namespace = nsOpt
      EnclosingModules = []
      TypeName = name
      Kind = Record (fields |> List.map mkField)
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

/// Build a record TypeInfo nested inside enclosing modules. `enclosingModules`
/// represents the chain of modules under `namespacePath` that contain the type
/// (e.g. namespacePath="MyApp.Domain", enclosingModules=["Auth"] for a type
/// declared inside `module Auth = ...` under `namespace MyApp.Domain`).
let nestedRecord
    (namespacePath: string)
    (enclosingModules: string list)
    (name: string)
    (fields: (string * TypeInfo) list)
    : TypeInfo
    =
    let nsOpt = if System.String.IsNullOrEmpty namespacePath then None else Some namespacePath
    { Namespace = nsOpt
      EnclosingModules = enclosingModules
      TypeName = name
      Kind = Record (fields |> List.map mkField)
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

/// Build a single-case wrapper union, e.g. `type ProductId = ProductId of int`.
let wrapperUnion (namespacePath: string) (name: string) (caseName: string) (innerTi: TypeInfo) : TypeInfo =
    let nsOpt = if System.String.IsNullOrEmpty namespacePath then None else Some namespacePath
    let case : UnionCase = {
        CaseName = caseName
        Fields = [ { Name = "Item"; Type = innerTi; Attributes = [] } ]
        Tag = None
        Attributes = []
    }
    { Namespace = nsOpt
      EnclosingModules = []
      TypeName = name
      Kind = Union [ case ]
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

/// Build a multi-case union. Each case is (caseName, list of field TypeInfos).
let multiUnion (namespacePath: string) (name: string) (cases: (string * TypeInfo list) list) : TypeInfo =
    let nsOpt = if System.String.IsNullOrEmpty namespacePath then None else Some namespacePath
    let unionCases =
        cases
        |> List.map (fun (caseName, fieldTis) ->
            let fields =
                fieldTis
                |> List.mapi (fun i ti ->
                    { Name = (if fieldTis.Length = 1 then "Item" else sprintf "Item%d" (i + 1))
                      Type = ti
                      Attributes = [] })
            { CaseName = caseName; Fields = fields; Tag = None; Attributes = [] } : UnionCase)
    { Namespace = nsOpt
      EnclosingModules = []
      TypeName = name
      Kind = Union unionCases
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

/// Build an enum with no-payload cases.
let enumTi (namespacePath: string) (name: string) (cases: string list) : TypeInfo =
    let nsOpt = if System.String.IsNullOrEmpty namespacePath then None else Some namespacePath
    let enumCases =
        cases
        |> List.mapi (fun i caseName ->
            { CaseName = caseName; Value = i; Attributes = [] } : EnumCase)
    { Namespace = nsOpt
      EnclosingModules = []
      TypeName = name
      Kind = Enum enumCases
      Attributes = []
      GenericParameters = []
      GenericArguments = [] }

// ── SerdeTypeInfo ──────────────────────────────────────────────────────────

let toSerde (ti: TypeInfo) : SerdeTypeInfo =
    SerdeMetadataBuilder.buildSerdeTypeInfo ti

// ── RPC method / interface ─────────────────────────────────────────────────

/// Build an RpcMethodInfo where input is a single (non-tupled) parameter.
let methodOf
    (methodName: string)
    (inputTi: TypeInfo)
    (outputTi: TypeInfo)
    : RpcMethodInfo
    =
    { MethodName = methodName
      InputType = typeInfoToFqFSharpType inputTi
      InputIsTupled = false
      InputParams = []
      OutputType = typeInfoToFqFSharpType outputTi
      InputTypeInfo = Some inputTi
      OutputTypeInfo = Some outputTi
      InputParamTypeInfos = [] }

/// Build an RpcMethodInfo with unit input.
let nullaryMethod
    (methodName: string)
    (outputTi: TypeInfo)
    : RpcMethodInfo
    =
    { MethodName = methodName
      InputType = "unit"
      InputIsTupled = false
      InputParams = []
      OutputType = typeInfoToFqFSharpType outputTi
      InputTypeInfo = Some unitTi
      OutputTypeInfo = Some outputTi
      InputParamTypeInfos = [] }

/// Build an RpcMethodInfo with multi-arg (tupled) input.
let tupledMethod
    (methodName: string)
    (paramTis: TypeInfo list)
    (outputTi: TypeInfo)
    : RpcMethodInfo
    =
    let inputStr =
        paramTis
        |> List.map typeInfoToFqFSharpType
        |> String.concat " * "
    { MethodName = methodName
      InputType = inputStr
      InputIsTupled = true
      InputParams = paramTis |> List.map typeInfoToFqFSharpType
      OutputType = typeInfoToFqFSharpType outputTi
      InputTypeInfo = Some (tupTi paramTis)
      OutputTypeInfo = Some outputTi
      InputParamTypeInfos = paramTis |> List.map Some }

/// Build an RpcInterfaceInfo placed in `namespacePath` with the given methods.
/// `isParentNamespace` controls the emitter's namespace-vs-module branch
/// (default true = `namespace Foo.Bar` style).
let interfaceOf
    (namespacePath: string)
    (shortName: string)
    (methods: RpcMethodInfo list)
    (isParentNamespace: bool)
    : RpcInterfaceInfo
    =
    let fullName =
        if System.String.IsNullOrEmpty namespacePath then shortName
        else namespacePath + "." + shortName
    { FullName = fullName
      ShortName = shortName
      Methods = methods
      Root = None
      Version = None
      UrlCaseValue = 0
      GenerateFableClient = true
      FableOutputDir = None
      SourceFilePath = None
      IsParentNamespace = isParentNamespace }
