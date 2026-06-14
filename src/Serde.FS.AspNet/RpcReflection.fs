namespace Serde.FS.Json.AspNet

open System
open System.Threading.Tasks

/// Loads generated SerdeGenerated.Rpc.<ApiName> modules by reflection
/// and invokes their static functions for RPC dispatch.
module internal RpcReflection =

    /// Load the generated RPC module type for the given API interface name.
    let loadModule (apiName: string) : Type =
        let fullName = $"SerdeGenerated.Rpc.%s{apiName}"
        let mutable found : Type option = None
        for asm in AppDomain.CurrentDomain.GetAssemblies() do
            if found.IsNone then
                match asm.GetType(fullName) with
                | null -> ()
                | t -> found <- Some t
        match found with
        | Some t -> t
        | None -> failwith $"RPC module '%s{fullName}' not found. Ensure the project references Serde.FS.Json and the [<RpcApi>] interface is visible to the source generator."

    /// Get the list of RPC method names from the generated module.
    let getMethods (rpcModule: Type) : string list =
        rpcModule.GetProperty("methods").GetValue(null) :?> string list

    /// Deserialize JSON input for a given RPC method.
    let deserializeDynamic (rpcModule: Type) (methodName: string) (json: string) : obj =
        rpcModule.GetMethod("deserializeDynamic").Invoke(null, [| methodName; json |])

    /// Serialize output for a given RPC method to JSON.
    let serializeDynamic (rpcModule: Type) (methodName: string) (value: obj) : string =
        rpcModule.GetMethod("serializeDynamic").Invoke(null, [| methodName; value |]) :?> string

    /// Invoke an RPC method on the implementation object.
    let invoke (rpcModule: Type) (impl: obj) (methodName: string) (input: obj) : Task<obj> =
        rpcModule.GetMethod("invoke").Invoke(null, [| impl; methodName; input |]) :?> Task<obj>
