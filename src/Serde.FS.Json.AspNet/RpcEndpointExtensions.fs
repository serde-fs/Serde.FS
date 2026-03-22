namespace Serde.FS.Json.AspNet

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

module private Helpers =
    let readBodyAsString (ctx: HttpContext) =
        task {
            use sr = new System.IO.StreamReader(ctx.Request.Body)
            return! sr.ReadToEndAsync()
        }

    let writeJson (ctx: HttpContext) (json: string) =
        ctx.Response.ContentType <- "application/json"
        ctx.Response.WriteAsync(json)

[<AutoOpen>]
module RpcEndpointExtensions =

    type IEndpointRouteBuilder with
        member this.MapRpcApi<'TApi>(impl: 'TApi) =
            let apiName = typeof<'TApi>.Name
            let rpcModule = RpcReflection.loadModule apiName

            let group = this.MapGroup("/rpc")

            for methodName in RpcReflection.getMethods rpcModule do
                group.MapPost(methodName, RequestDelegate(fun ctx ->
                    task {
                        let! body = Helpers.readBodyAsString ctx
                        let input = RpcReflection.deserializeDynamic rpcModule methodName body
                        let! output = RpcReflection.invoke rpcModule (impl :> obj) methodName input
                        let json = RpcReflection.serializeDynamic rpcModule methodName output
                        return! Helpers.writeJson ctx json
                    } :> System.Threading.Tasks.Task
                ))
                |> ignore

            this
