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
[<AbstractClass; Sealed>]
type Bootstrap private () =

    static let gate = obj ()
    static let ran = HashSet<Type>()

    static let loadableTypes (asm: Assembly) =
        if asm.IsDynamic then
            [||]
        else
            try
                asm.GetTypes()
            with
            | :? ReflectionTypeLoadException as ex ->
                ex.Types |> Array.filter (fun t -> not (isNull t))
            | _ -> [||]

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

    static let runIn (propagateInitErrors: bool) (assemblies: Assembly seq) =
        lock gate (fun () ->
            for asm in assemblies do
                for ty in candidateTypes asm do
                    if typeof<IEntryPointBootstrap>.IsAssignableFrom(ty)
                       && not ty.IsInterface
                       && not ty.IsAbstract
                       && ran.Add(ty) then
                        try
                            (Activator.CreateInstance(ty) :?> IEntryPointBootstrap).Init()
                        with _ ->
                            // Un-mark the type so a later call can retry it.
                            ran.Remove(ty) |> ignore
                            if propagateInitErrors then reraise ())

    /// Force-loads the entry assembly's references, then runs every
    /// IEntryPointBootstrap found in the currently loaded assemblies.
    /// Failures (an assembly that cannot be reflected, or a failing Init) are
    /// swallowed so one bad bootstrap cannot crash startup; a failed bootstrap
    /// is retried on the next call.
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
