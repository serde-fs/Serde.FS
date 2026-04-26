namespace SampleRpc.Shared

open Serde.FS

// DTOs — no [<Serde>] needed, discovered via [<RpcApi>] interface

// Type abbreviations: must be expanded by the source generator so that
// codecs are emitted for the underlying types (e.g., int) rather than
// the alias names.
type PageSize = int
type PageNumber = int

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
[<GenerateFableClient>]
type IOrderApi =
    abstract GetProduct : int -> Async<Product>
    abstract TryGetProduct : int -> Async<Result<Product, string>>
    abstract PlaceOrder : Order -> Async<OrderSummary>
    abstract ListProducts : unit -> Async<Product list>
    /// Multi-arg method using type abbreviations; exercises both alias resolution
    /// and the multi-arg interface override path in the Fable client emitter.
    abstract ListProductsPage : PageSize * PageNumber -> Async<Product list>
