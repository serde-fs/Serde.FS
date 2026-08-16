module SampleRpc.Server.InventoryApi

open SampleRpc.Shared

/// A request-scoped dependency, registered via `AddScoped` in Program.fs.
/// Exists to demonstrate that `MapRpcApi<IInventoryApi>()` resolves the API
/// implementation (and its dependencies) from the request's DI scope.
type IStockRepository =
    abstract GetLevel : ProductId -> StockLevel

type InMemoryStockRepository() =
    interface IStockRepository with
        member _.GetLevel productId =
            { Quantity = 12 + productId.Value % 5; ReorderThreshold = 5 }

/// Constructed by the DI container per request; see `AddScoped<IInventoryApi, InventoryApi>()`.
type InventoryApi(stock: IStockRepository) =
    interface IInventoryApi with
        member _.GetStock productId =
            async {
                return {
                    ProductId = productId
                    Level = stock.GetLevel productId
                    Location = $"Aisle %d{productId.Value % 10}"
                }
            }

        member _.ListLowStock threshold =
            async {
                return [
                    for i in 1 .. 3 ->
                        { ProductId = { Value = i }
                          Level = { Quantity = max 0 (threshold - i); ReorderThreshold = threshold }
                          Location = $"Aisle %d{i}" }
                ]
            }

        member _.GetStockedProduct productId =
            async {
                return {
                    Id = productId
                    Name = $"In-stock product #{productId.Value}"
                    Price = decimal productId.Value * 9.99m
                    Tags = [ "in-stock" ]
                } : Product
            }
