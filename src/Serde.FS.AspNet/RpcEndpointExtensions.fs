namespace Serde.FS.AspNet

open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection

module private Helpers =
    /// Transforms a method name according to the UrlCase setting.
    let applyUrlCase (urlCase: Serde.FS.UrlCase) (methodName: string) =
        match urlCase with
        | Serde.FS.UrlCase.Kebab ->
            let chars =
                methodName
                |> Seq.collect (fun c ->
                    if System.Char.IsUpper c then
                        seq { '-'; System.Char.ToLowerInvariant c }
                    else
                        seq { c })
                |> Seq.toArray
            let s = System.String(chars)
            if s.StartsWith("-") then s.Substring(1) else s
        | _ -> methodName

    let readBodyAsString (ctx: HttpContext) =
        task {
            use sr = new System.IO.StreamReader(ctx.Request.Body)
            return! sr.ReadToEndAsync()
        }

    let writeJson (ctx: HttpContext) (json: string) =
        ctx.Response.ContentType <- "application/json"
        ctx.Response.WriteAsync(json)

type RpcRoute =
    { 
        MethodName : string
        Builder : IEndpointConventionBuilder 
    }

    /// Adds GET as an additional allowed verb (POST is always included for RPC).
    member this.AllowGet() =
        this.Builder.WithMetadata(HttpMethodMetadata([| "POST"; "GET" |])) |> ignore
        this

    /// Adds PUT as an additional allowed verb (POST is always included for RPC).
    member this.UsePut() =
        this.Builder.WithMetadata(HttpMethodMetadata([| "POST"; "PUT" |])) |> ignore
        this

    /// Adds DELETE as an additional allowed verb (POST is always included for RPC).
    member this.UseDelete() =
        this.Builder.WithMetadata(HttpMethodMetadata([| "POST"; "DELETE" |])) |> ignore
        this

    /// Applies an authorization policy to this RPC route.
    member this.RequireAuthorization(policy: string) =
        this.Builder.RequireAuthorization(policy) |> ignore
        this


/// Wraps the route group and per-method endpoint builders returned by MapRpcApi.
type RpcApiBuilder =
    {
        Group: IEndpointRouteBuilder
        Endpoints: Dictionary<string, IEndpointConventionBuilder>
    }
    /// Look up a route by method name for fluent configuration (e.g., RequireAuthorization).
    /// Use with nameof: rpc.GetRoute(nameof Unchecked.defaultof<IOrderApi>.GetProduct)
    member this.GetRoute(methodName: string) =
        match this.Endpoints.TryGetValue(methodName) with
        | true, builder -> builder
        | false, _ ->
            let available = System.String.Join(", ", this.Endpoints.Keys)
            failwith $"RPC method '%s{methodName}' not found. Available methods: %s{available}"

    member this.ApplyRouteConventions(apply: RpcRoute -> unit) =
        for KeyValue(methodName, builder) in this.Endpoints do
            let route = { MethodName = methodName; Builder = builder }
            apply route


module private RpcEndpointCore =

    /// Maps every method of the [<RpcApi>] interface 'TApi to a POST endpoint.
    /// `resolve` is invoked once per request to obtain the implementation that
    /// handles that request, so implementations may be singletons, per-request
    /// instances, or resolved from the request's DI scope.
    let mapRpcApi<'TApi> (routes: IEndpointRouteBuilder) (resolve: HttpContext -> 'TApi) =
        let apiType = typeof<'TApi>
        let apiName = apiType.Name
        let rpcModule = RpcReflection.loadModule apiName

        // Ensure the generated bootstraps (JSON codecs + RPC client factories)
        // have run, even when the source-generated entry point never executed
        // (in-process Kestrel hosts, WebApplicationFactory tests, add-ins, C# hosts).
        Serde.FS.Bootstrap.Run(rpcModule.Assembly)
        if not (obj.ReferenceEquals(apiType.Assembly, rpcModule.Assembly)) then
            Serde.FS.Bootstrap.Run(apiType.Assembly)

        // Read [<RpcApi>] attribute for Root and Version
        let rpcAttr =
            apiType.GetCustomAttributes(typeof<Serde.FS.RpcApiAttribute>, false)
            |> Array.tryHead
            |> Option.map (fun a -> a :?> Serde.FS.RpcApiAttribute)

        let root =
            match rpcAttr |> Option.bind (fun a -> Option.ofObj a.Root) with
            | Some r when r.Length > 0 -> r
            | _ -> apiName

        let versionSegment =
            match rpcAttr |> Option.bind (fun a -> Option.ofObj a.Version) with
            | Some v when v.Length > 0 -> $"/%s{v}"
            | _ -> ""

        let urlCase =
            rpcAttr
            |> Option.map (fun a -> a.UrlCase)
            |> Option.defaultValue Serde.FS.UrlCase.Default

        let routePrefix = $"/rpc/%s{root}%s{versionSegment}"
        let group = routes.MapGroup(routePrefix)
        let endpoints = Dictionary<string, IEndpointConventionBuilder>()

        for methodName in RpcReflection.getMethods rpcModule do
            let methodSegment = Helpers.applyUrlCase urlCase methodName
            let builder =
                group.MapPost(methodSegment, RequestDelegate(fun ctx ->
                    task {
                        let impl = resolve ctx
                        let! body = Helpers.readBodyAsString ctx
                        let input = RpcReflection.deserializeDynamic rpcModule methodName body
                        let! output = RpcReflection.invoke rpcModule (box impl) methodName input
                        let json = RpcReflection.serializeDynamic rpcModule methodName output
                        return! Helpers.writeJson ctx json
                    } :> System.Threading.Tasks.Task
                ))
            endpoints.[methodName] <- builder

        { Group = routes; Endpoints = endpoints }


[<AutoOpen>]
module RpcEndpointExtensions =

    type IEndpointRouteBuilder with

        /// Maps the [<RpcApi>] interface 'TApi to endpoints served by a single
        /// implementation instance shared across all requests.
        member this.MapRpcApi<'TApi>(impl: 'TApi) =
            RpcEndpointCore.mapRpcApi<'TApi> this (fun _ -> impl)

        /// Maps the [<RpcApi>] interface 'TApi to endpoints whose implementation
        /// is created per request by `factory`. Use this to build the implementation
        /// from request-scoped state without registering it in DI, e.g.
        /// `app.MapRpcApi<IOrderApi>(fun ctx -> OrderApi(ctx.RequestServices.GetRequiredService<IProductRepository>()) :> IOrderApi)`.
        /// Note: as with ASP.NET's own `AddSingleton<T>(factory)` overload, an F# lambda
        /// returning a class must upcast to 'TApi (`:> IOrderApi`); object expressions and
        /// functions already typed as `HttpContext -> IOrderApi` need no upcast.
        member this.MapRpcApi<'TApi>(factory: System.Func<HttpContext, 'TApi>) =
            RpcEndpointCore.mapRpcApi<'TApi> this (fun ctx -> factory.Invoke ctx)

        /// Maps the [<RpcApi>] interface 'TApi to endpoints whose implementation is
        /// resolved from the request's service scope (`ctx.RequestServices`) on every
        /// call. Register the implementation with the host's DI container first, e.g.
        /// `builder.Services.AddScoped<IOrderApi, OrderApi>()`; scoped and transient
        /// dependencies are then honored per request.
        member this.MapRpcApi<'TApi>() =
            RpcEndpointCore.mapRpcApi<'TApi> this (fun ctx -> ctx.RequestServices.GetRequiredService<'TApi>())
