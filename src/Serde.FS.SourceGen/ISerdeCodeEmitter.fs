namespace Serde.FS

open FSharp.SourceDjinn.TypeModel.Types

type ISerdeCodeEmitter =
    abstract member Emit : SerdeTypeInfo -> string
    /// File suffix for per-type generated files (e.g. "json"). Produces "{TypeName}.{suffix}.g.fs".
    abstract member HintNameSuffix : string

type ISerdeResolverEmitter =
    abstract member EmitResolver : SerdeTypeInfo list -> string option
    /// The hint name for the resolver file (e.g. "~SerdeResolver.serde.g.fs").
    abstract member ResolverHintName : string
    /// Additional files to emit after the resolver (e.g. registration + bootstrap). Returns (hintName, code) pairs.
    abstract member EmitRegistrationFiles : unit -> (string * string) list
    /// Whether this backend emits per-type files (true) or consolidates all codecs into the resolver (false).
    abstract member EmitPerTypeFiles : bool

/// Metadata for a single RPC method discovered from an [<RpcApi>] interface.
type RpcMethodInfo = {
    MethodName: string
    /// F# type expression for the input parameter (e.g., "int", "SampleRpc.Order").
    /// For multi-arg methods declared as `A * B -> C`, this is the composite tuple type "A * B".
    /// Used by string-driven emitters (e.g., RpcDispatchEmitter splices it into
    /// `SerdeJson.deserialize<%s>`); structural emitters should prefer InputTypeInfo.
    InputType: string
    /// True when the abstract member was declared with a top-level tuple input
    /// (e.g., `abstract Foo: A * B -> C`), which F# treats as a multi-arg method.
    /// False when declared with a single (possibly paren-wrapped) input.
    InputIsTupled: bool
    /// When InputIsTupled is true, the per-parameter F# type expressions
    /// (e.g., ["A"; "B"]). Empty otherwise.
    InputParams: string list
    /// F# type expression for the return type, unwrapped from Async/Task (e.g., "SampleRpc.Product").
    /// See note on InputType — structural emitters should prefer OutputTypeInfo.
    OutputType: string
    /// Structural TypeInfo for the input parameter, populated by RpcApiDiscovery.
    /// None when the type could not be resolved against the discovery lookup
    /// (the Fable emitter surfaces this as a build error).
    InputTypeInfo: TypeInfo option
    /// Structural TypeInfo for the return type (unwrapped from Async/Task).
    OutputTypeInfo: TypeInfo option
    /// When InputIsTupled is true, the per-parameter TypeInfos in declaration order.
    /// Empty otherwise. An entry can itself be None for an unresolved per-parameter type.
    InputParamTypeInfos: TypeInfo option list
}

/// Metadata for an [<RpcApi>] interface.
type RpcInterfaceInfo = {
    /// Fully qualified interface name (e.g., "SampleRpc.IOrderApi")
    FullName: string
    /// Short interface name (e.g., "IOrderApi")
    ShortName: string
    /// Methods in declaration order
    Methods: RpcMethodInfo list
    /// Custom root from [<RpcApi(Root = "...")>]. None means use interface name.
    Root: string option
    /// Version segment from [<RpcApi(Version = "...")>]. None means no version.
    Version: string option
    /// UrlCase value from [<RpcApi(UrlCase = ...)>]. 0=Default, 1=Kebab.
    UrlCaseValue: int
    /// Absolute path to the .fs file declaring the interface (when known).
    /// Used by emitters for MSBuild-format error diagnostics.
    SourceFilePath: string option
    /// True when the interface's enclosing scope is a namespace, false when
    /// it is a top-level module (`module Foo.Bar`). Emitters that produce a
    /// sibling module (e.g. the Fable client) need to know which shape to
    /// generate because F# disallows multi-file additions to a top-level
    /// module.
    IsParentNamespace: bool
}

/// Result of RPC API discovery.
type RpcDiscoveryResult = {
    /// Types that need codec generation
    DiscoveredTypes: SerdeTypeInfo list
    /// Interface metadata for RPC dispatch module generation
    Interfaces: RpcInterfaceInfo list
    /// Names of F# type abbreviations (`type Foo = Guid`) discovered in the
    /// source files. Aliases erase at compile time so they don't appear in
    /// DiscoveredTypes, but the validator needs to recognise them so a field
    /// `Id: SheetNumber` doesn't trigger "SheetNumber has no Serde metadata".
    AliasNames: Set<string>
    /// Normalize a parser-captured field TypeInfo into its canonical form:
    ///   • partial qualifier "Forge.Hub" → resolved FQN parts via suffix lookup
    ///   • alias name "SheetNumber" → underlying target TypeInfo (e.g. Primitive Guid)
    ///   • unqualified name with ambiguous short name (e.g. "Conduit" when both
    ///     `ConduitSchedule.Conduit` and `FeederRelease.Conduit` exist) →
    ///     resolved against `parentScope` first (the FQN segments of the
    ///     containing record/union), mirroring F#'s lexical scoping
    ///   • already-resolved or built-in types → returned unchanged
    /// Used by the codec emitter so generated F# code references types by a
    /// form that compiles in the generated module's scope.
    /// First arg is `parentScope` — the FQN segments of the containing
    /// record/union (used to disambiguate ambiguous short names via F#'s
    /// lexical-scope rules). Pass [] when there's no containing type.
    ResolveFieldType: string list -> TypeInfo -> TypeInfo
}

/// Result of cross-project emission for an RPC backend.
///   Files  — (absolutePath, code) pairs to write to disk.
///   Errors — MSBuild-format diagnostic strings of the form
///            `"path(line,col): error CODE: message"`. The GeneratorHost
///            forwards these verbatim so MSBuild surfaces them as clickable
///            compile errors in the user's IDE.
type CrossProjectEmitResult = {
    Files: (string * string) list
    Errors: string list
}

type ISerdeRpcEmitter =
    /// Emit RPC dispatch modules for [<RpcApi>] interfaces.
    /// Returns (hintName, code) pairs for each interface.
    abstract member EmitRpcModules : RpcInterfaceInfo list -> (string * string) list
    /// Emit cross-project files (e.g. Fable client) for [<RpcApi>] interfaces.
    abstract member EmitCrossProjectFiles : RpcInterfaceInfo list * SerdeTypeInfo list -> CrossProjectEmitResult

module SerdeCodegenRegistry =
    let mutable private defaultEmitter : ISerdeCodeEmitter option = None
    let setDefaultEmitter emitter = defaultEmitter <- Some emitter
    let getDefaultEmitter () = defaultEmitter
