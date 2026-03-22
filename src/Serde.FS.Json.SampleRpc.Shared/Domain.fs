namespace SampleRpc.Shared

open Serde.FS

// DTOs — no [<Serde>] needed, discovered via [<RpcApi>] interface

[<Struct>]
type ProductId = { Value: int }

type Product = {
    Id: ProductId
    Name: string
    Price: decimal
    Tags: string list
}

type OrderLine = {
    Product: Product
    Quantity: int
}

type Order = {
    Id: int
    Lines: OrderLine list
    Notes: string option
}

type OrderSummary = {
    OrderId: int
    TotalItems: int
    TotalPrice: decimal
}

// RPC API contract — this is the single entry point for codec generation

[<RpcApi>]
type IOrderApi =
    abstract GetProduct : int -> Async<Product>
    abstract PlaceOrder : Order -> Async<OrderSummary>
    abstract ListProducts : unit -> Async<Product list>
