namespace FSharp.SourceDjinn.TypeModel

/// Defines a discoverable bootstrap that FSharp.SourceDjinn will automatically run in its [<EntryPoint>] function. 
/// This allows libraries to perform necessary initialization without requiring users to call specific methods.
/// Any generated type that implements this interface will be instantiated and its `Init` method invoked during Djinn�s startup sequence. 
/// This allows libraries and code generators to register metadata, configure backends,
/// or perform other one-time initialization without relying on naming onventions or a single global bootstrap entry point.
type IEntryPointBootstrap =
    abstract member Init : unit -> unit
