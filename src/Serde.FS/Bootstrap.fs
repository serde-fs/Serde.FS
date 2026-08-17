namespace Serde.FS

open System
open System.Collections.Generic
open System.Reflection

/// Discovers and runs IEntryPointBootstrap implementations (the generated JSON codec
/// and RPC client registrations). Safe to call any number of times: each implementor
/// type runs Init() at most once per process.
///
/// Apps that use [&lt;Serde.FS.EntryPoint&gt;] never need to call this directly - the
/// source-generated entry point does. It exists for hosts where that entry point
/// never runs: desktop shells embedding Kestrel in-process, add-ins loaded into a
/// host application (AutoCAD, Revit, ...), WebApplicationFactory-style test hosts,
/// or C# hosts referencing the API assembly as a library. RPC entry points
/// (MapRpcApi, RpcClient.create) call this automatically.
module private BootstrapJustifications =
    [<Literal>]
    let LegacyScan =
        "Legacy full-type scan for assemblies generated before the [<SerdeBootstrap>] attribute existed. " +
        "Generated assemblies declare their bootstraps via that attribute (annotated to keep the constructor), " +
        "which is the path used under trimming / Native AOT; the scan there is best-effort and failures are swallowed."

[<AbstractClass; Sealed>]
type Bootstrap private () =

    static let gate = obj ()
    static let ran = HashSet<Type>()

    /// Framework / runtime assemblies never contain generated bootstraps, and
    /// under Native AOT enumerating their types and probing them with
    /// IsAssignableFrom can throw TypeLoadException for internal generic
    /// helpers that carry no reflection metadata. They are excluded from the
    /// legacy full-type scan only — the [<SerdeBootstrap>] attribute check
    /// below still runs for every assembly.
    static let isFrameworkAssembly (asm: Assembly) =
        let name = try asm.GetName().Name with _ -> ""
        isNull name
        || name = "mscorlib" || name = "netstandard" || name = "FSharp.Core"
        || name.StartsWith("System.", StringComparison.Ordinal) || name = "System"
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)

    [<System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = BootstrapJustifications.LegacyScan)>]
    static let loadableTypes (asm: Assembly) =
        if asm.IsDynamic || isFrameworkAssembly asm then
            [||]
        else
            try
                asm.GetTypes()
            with
            | :? ReflectionTypeLoadException as ex ->
                ex.Types |> Array.filter (fun t -> not (isNull t))
            | _ -> [||]

    /// Instantiates a bootstrap type. Types reach here either from
    /// [<SerdeBootstrap>] attributes (constructor preserved by annotation) or from
    /// the legacy scan (JIT only), so the unannotated flow is expected.
    [<System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2067", Justification = BootstrapJustifications.LegacyScan)>]
    [<System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072", Justification = BootstrapJustifications.LegacyScan)>]
    static let instantiate (ty: Type) : IEntryPointBootstrap =
        Activator.CreateInstance(ty) :?> IEntryPointBootstrap

    /// Bootstrap candidates for an assembly: types declared via assembly-level
    /// [<SerdeBootstrap>] attributes (Native AOT / trimming safe — the attribute
    /// roots the type and its constructor), unioned with the legacy full-type
    /// scan so assemblies generated before the attribute existed keep working.
    static let candidateTypes (asm: Assembly) =
        let fromAttributes =
            try
                asm.GetCustomAttributes(typeof<SerdeBootstrapAttribute>, false)
                |> Array.choose (function
                    | :? SerdeBootstrapAttribute as a when not (isNull a.BootstrapType) -> Some a.BootstrapType
                    | _ -> None)
            with _ -> [||]
        Array.append fromAttributes (loadableTypes asm) |> Array.distinct

    /// True when `ty` is a concrete IEntryPointBootstrap implementor. Reflection
    /// probes can throw for exotic types (Native AOT metadata gaps, unloadable
    /// generic instantiations); those are simply not bootstraps.
    static let isBootstrapType (ty: Type) =
        try
            typeof<IEntryPointBootstrap>.IsAssignableFrom(ty)
            && not ty.IsInterface
            && not ty.IsAbstract
        with _ -> false

    static let runIn (propagateInitErrors: bool) (assemblies: Assembly seq) =
        lock gate (fun () ->
            for asm in assemblies do
                for ty in candidateTypes asm do
                    if isBootstrapType ty && ran.Add(ty) then
                        try
                            (instantiate ty).Init()
                        with _ ->
                            // Un-mark the type so a later call can retry it.
                            ran.Remove(ty) |> ignore
                            if propagateInitErrors then reraise ())

    /// Force-loads the entry assembly's references, then runs every
    /// IEntryPointBootstrap found in the currently loaded assemblies.
    /// Failures (an assembly that cannot be reflected, or a failing Init) are
    /// swallowed so one bad bootstrap cannot crash startup; a failed bootstrap
    /// is retried on the next call.
    [<System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "GetReferencedAssemblies is best-effort discovery of not-yet-loaded assemblies for the JIT scan; " +
                        "it is wrapped in try/with and simply yields nothing under trimming / Native AOT, where the generated " +
                        "entry point registers everything through the direct Run(bootstraps) overload.")>]
    static member Run() =
        let entry = Assembly.GetEntryAssembly()
        if not (isNull entry) then
            // Native AOT: GetReferencedAssemblies() throws
            // PlatformNotSupportedException (issue #13). There everything the
            // app needs is registered by the generated entry point's direct
            // Run(bootstraps) call; this scan then only covers assemblies that
            // are already loaded.
            let refs = try entry.GetReferencedAssemblies() with _ -> [||]
            for name in refs do
                try Assembly.Load(name) |> ignore with _ -> ()
        runIn false (AppDomain.CurrentDomain.GetAssemblies())

    /// Runs the given bootstrap instances directly — no reflection discovery.
    /// The source-generated entry point calls this with the bootstraps of its
    /// own compilation, so they are statically rooted and always run, even
    /// under Native AOT / trimming where the scan-based overloads find nothing.
    /// Each bootstrap type still runs at most once per process (the tracking
    /// is shared with the scan-based overloads), and Init errors propagate.
    static member Run(bootstraps: IEntryPointBootstrap[]) =
        lock gate (fun () ->
            for b in bootstraps do
                let ty = b.GetType()
                if ran.Add(ty) then
                    try
                        b.Init()
                    with _ ->
                        // Un-mark the type so a later call can retry it.
                        ran.Remove(ty) |> ignore
                        reraise ())

    /// Runs the IEntryPointBootstrap implementations declared in the given
    /// assembly only. A targeted call expresses explicit intent, so Init()
    /// exceptions propagate to the caller instead of surfacing later as a
    /// confusing missing-codec error.
    static member Run(assembly: Assembly) =
        runIn true [ assembly ]
