# ✅ **Mini‑Spec: Switch .NET RpcClient to Task\<T\> and Update SampleRpc.Client**

### **Goal**
Update the .NET RpcClient so that generated client proxies expose `Task<'T>` instead of `Async<'T>`, while keeping the shared interface defined in terms of `Async<'T>`.

### **Requirements**

### **1. RpcClient proxy should expose Task\<T\>**
For every RPC method:

- Input: same as interface  
- Output: **Task\<T\>**, not Async\<T\>  
- Internally convert the interface’s Async\<T\> to Task\<T\> using `Async.StartAsTask`

Example:

```fsharp
member _.GetProduct(id: ProductId) : Task<Product> =
    async {
        let! result = callServer id
        return result
    }
    |> Async.StartAsTask
```

### **2. Do NOT change the shared interface**
The interface in `SampleRpc.Shared` stays:

```fsharp
abstract GetProduct : ProductId -> Async<Product>
```

### **3. Update SampleRpc.Client to remove Async.StartAsTask calls**
Because the proxy now returns `Task<'T>`, the sample client should call:

```fsharp
let! product = orders.GetProduct(ProductId 42)
```

No more:

```fsharp
orders.GetProduct 42 |> Async.StartAsTask
```

### **4. No structural changes**
Only:

- RpcClient generator  
- SampleRpc.Client Program.fs  

Nothing else.

---
