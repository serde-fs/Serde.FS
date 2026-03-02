namespace Serde

module ResolverBootstrap =
    let mutable registerAll : (unit -> unit) option = None
