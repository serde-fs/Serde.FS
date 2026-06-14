module SampleRpc.Server.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Authentication
open SampleRpc.Shared
open SampleRpc.Server.OrderApi
open SampleRpc.Server.InventoryApi
open Serde.FS.AspNet

module Handlers =
    open System.Security.Claims

    type ApiKeyAuthHandler(options, logger, encoder) =
        inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

        override this.HandleAuthenticateAsync() =
            let request = this.Request
            task {
                match request.Headers.TryGetValue("X-Api-Key") with
                | true, v when v.ToString() = "ABC" ->
                    let claims = [| Claim(ClaimTypes.Name, "ApiKeyUser") |]
                    let identity = ClaimsIdentity(claims, "ApiKey")
                    let principal = ClaimsPrincipal(identity)
                    let ticket = AuthenticationTicket(principal, "ApiKey")
                    return AuthenticateResult.Success(ticket)
                | _ ->
                    return AuthenticateResult.Fail("Missing or invalid X-Api-Key header")
            }


let (|StartsWith|_|) (prefix: string) (s: string) =
    if s.StartsWith(prefix) then Some () else None

[<Serde.FS.EntryPoint>]
let main (argv: string array) =
    let builder = WebApplication.CreateBuilder(argv)

    builder.Services
        .AddAuthentication("ApiKey")
        .AddScheme<AuthenticationSchemeOptions, Handlers.ApiKeyAuthHandler>("ApiKey", ignore)
    |> ignore

    builder.Services.AddAuthorization(fun options ->
        options.AddPolicy("ApiKeyPolicy", fun policy ->
            policy.RequireAuthenticatedUser() |> ignore
        )
    ) |> ignore

    let app = builder.Build()
    app.UseAuthentication() |> ignore
    app.UseAuthorization() |> ignore

    let rpc = app.MapRpcApi<IOrderApi>(OrderApi())
    // Second [<RpcApi>] mapped on the same app — verifies that two interfaces
    // get isolated route prefixes (/rpc/IOrderApi/... vs /rpc/IInventoryApi/...)
    // and that their generated codec modules don't collide on shared types
    // (ProductId, Product are referenced by both).
    app.MapRpcApi<IInventoryApi>(InventoryApi()) |> ignore

    rpc.ApplyRouteConventions(fun route ->
        match route.MethodName with
        | StartsWith "Get" | StartsWith "List" ->
            route.AllowGet() |> ignore
        | StartsWith "Delete" ->
            route.UseDelete() |> ignore
        | StartsWith "Update" ->
            route.UsePut() |> ignore
        | "PlaceOrder" ->
            route.RequireAuthorization("ApiKeyPolicy") |> ignore
        | _ ->
            printfn $"No convention for method '%s{route.MethodName}', using default routing and no authorization"
    )

    rpc.GetRoute(nameof Unchecked.defaultof<IOrderApi>.PlaceOrder)
        .RequireAuthorization("ApiKeyPolicy") |> ignore

    app.MapGet("/", System.Func<string>(fun () -> "Serde.FS.Json SampleApp — RPC endpoints at /rpc/{method}")) |> ignore

    printfn "Starting web server..."
    printfn "Use RpcApi.http to test the API"
    app.Run()

    0
