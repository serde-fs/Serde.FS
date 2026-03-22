# 📘 **CHANGE SPEC — Add `UrlCase` Support (Default + Kebab)**

## 🎯 Goal
Extend the `[<RpcApi>]` attribute to support an optional `UrlCase` setting that controls how method names are transformed into URL segments.

This affects **only** the *method name portion* of the route:

```
/rpc/{Root}/{Version?}/{MethodNameTransformed}
```

Defaults remain unchanged unless explicitly overridden.

---

# 1. **Update the RpcApiAttribute**

Add a new optional property:

```fsharp
member val UrlCase : UrlCase = UrlCase.Default with get, set
```

---

# 2. **Define the UrlCase union**

Create a new enum/DU:

```fsharp
type UrlCase =
    | Default   // Use method name exactly as declared (no transformation)
    | Kebab     // Convert to kebab-case (GetProducts -> get-products)
```

This is intentionally minimal — extensible later.

---

# 3. **Add a method-name transformation helper**

Implement a helper function somewhere appropriate (e.g., in `RpcEndpointExtensions.fs` or a small utility module):

```fsharp
let applyUrlCase (urlCase: UrlCase) (methodName: string) =
    match urlCase with
    | UrlCase.Default ->
        methodName
    | UrlCase.Kebab ->
        // Convert PascalCase or camelCase to kebab-case
        // Example: "GetProducts" -> "get-products"
        let chars =
            methodName
            |> Seq.collect (fun c ->
                if System.Char.IsUpper c then
                    seq { '-'; System.Char.ToLower c }
                else
                    seq { c })
            |> Seq.toArray

        let s = System.String(chars)
        if s.StartsWith "-" then s.Substring 1 else s
```

This is simple, predictable, and works for all typical F# method names.

---

# 4. **Integrate UrlCase into MapRpcApi**

Inside the loop that maps each method:

1. Read the attribute:

```fsharp
let urlCase = rpcApiAttribute.UrlCase
```

2. Transform the method name:

```fsharp
let methodSegment = applyUrlCase urlCase methodInfo.Name
```

3. Use `methodSegment` instead of `methodInfo.Name` when constructing the route:

```fsharp
let fullRoute = $"{routePrefix}/{methodSegment}"
```

Everything else stays the same.

---

# 5. **Client-side code generation**

The generated client must also apply the same transformation:

```fsharp
let methodSegment = applyUrlCase urlCase methodName
let url = $"{baseUrl}/{methodSegment}"
```

This ensures server and client stay in sync.

---

# 6. **Defaults**

If the user does not specify `UrlCase`, the behavior is unchanged:

```fsharp
[<RpcApi>]
type IOrderApi =
```

→

```
/rpc/IOrderApi/GetProducts
```

If they specify kebab:

```fsharp
[<RpcApi(Root = "orders", UrlCase = UrlCase.Kebab)>]
```

→

```
/rpc/orders/get-products
```

---

# 🌟 Summary

This spec adds:

- `UrlCase` property  
- `UrlCase.Default` and `UrlCase.Kebab`  
- A method-name transformer  
- Integration into both server and client routing  

No breaking changes.  
No behavioral changes unless explicitly opted in.  
Fully extensible for future cases.

---
