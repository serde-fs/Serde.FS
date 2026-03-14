# 🌿 **Spec C — Codec Registry & Codec Lookup**

## 🎯 **Purpose**
The Codec Registry is the central lookup table that maps:

```
Type → JsonCodec<'T>
```

It is the mechanism through which:

- Serde.FS.Json retrieves codecs  
- custom codecs override defaults  
- property‑level codecs override type‑level codecs  
- framework‑level codecs (RPC, Actor, etc.) plug in  
- Fable and .NET share the same codec model  

This registry is **runtime‑only** and does not depend on generators.

---

# 🌱 **1. Public API Surface**

### **1.1 Registry Type**
```fsharp
type CodecRegistry =
    {
        Codecs : Dictionary<Type, IJsonCodec>
    }
```

### **1.2 Creation**
```fsharp
module CodecRegistry =
    val empty : CodecRegistry
    val withPrimitives : unit -> CodecRegistry
    val add : Type * IJsonCodec -> CodecRegistry -> CodecRegistry
    val tryFind : Type -> CodecRegistry -> IJsonCodec option
```

### **1.3 Global Registry (optional but recommended)**
```fsharp
module GlobalCodecRegistry =
    val mutable Current : CodecRegistry
```

This allows:

- framework‑level registration  
- app‑level registration  
- test‑level registration  
- plugin‑level registration  

without passing the registry everywhere.

---

# 🌳 **2. Codec Interface**

### **2.1 Unified Codec Interface**
```fsharp
type IJsonCodec =
    abstract member Type : Type
    abstract member Encode : obj -> JsonNode
    abstract member Decode : JsonNode -> obj
```

### **2.2 Strongly Typed Version**
```fsharp
type JsonCodec<'T> =
    abstract member Encode : 'T -> JsonNode
    abstract member Decode : JsonNode -> 'T
```

### **2.3 Adapter**
Serde.FS.Json provides:

```fsharp
val boxCodec : JsonCodec<'T> -> IJsonCodec
```

---

# 🌿 **3. Lookup Rules**

### **3.1 Type-Level Lookup**
When serializing/deserializing type `'T`:

1. Look for a codec registered for `'T`.
2. If found → use it.
3. If not found → fall back to generated JsonTypeInfo.
4. If no generated metadata → throw SerdeMissingMetadataException.

### **3.2 Property-Level Override**
If a property has:

```fsharp
[<JsonCodec(typeof<MyCodec>)>]
```

Then:

1. Property-level codec overrides type-level codec.
2. Property-level codec overrides default codec.
3. Property-level codec overrides generated metadata.

This is resolved during JsonTypeInfo construction.

---

# 🌱 **4. Registry Initialization**

### **4.1 Primitive Codecs**
`CodecRegistry.withPrimitives()` registers:

- bool  
- int, int64  
- float, double, decimal  
- string  
- Guid  
- DateTime, DateTimeOffset  
- byte[], Memory<byte>  
- unit  

These are implemented as object expressions.

### **4.2 Framework Codecs**
Frameworks (RPC, Actor, etc.) can register:

- Option<'T>  
- Result<'T,'E>  
- Map  
- Set  
- custom transport types  

### **4.3 User Codecs**
Users can register:

```fsharp
GlobalCodecRegistry.Current <-
    GlobalCodecRegistry.Current
    |> CodecRegistry.add (typeof<MyType>, boxCodec myCodec)
```

---

# 🌳 **5. Integration with JsonTypeInfo**

### **5.1 JsonTypeInfo Factory**
When building a JsonTypeInfo for `'T`:

1. Check registry for `'T`.
2. If found → wrap codec into JsonTypeInfo.
3. If not found → use generated metadata.
4. Apply property-level overrides.
5. Return final JsonTypeInfo.

### **5.2 Codec → JsonTypeInfo Adapter**
Serde.FS.Json provides:

```fsharp
val codecToJsonTypeInfo : JsonCodec<'T> -> JsonTypeInfo<'T>
```

---

# 🌱 **6. Error Behavior**

### **6.1 Missing Codec**
If a codec is required but not found:

- If generated metadata exists → use it.
- If not → throw `SerdeMissingMetadataException`.

### **6.2 Duplicate Registration**
If a codec is registered twice for the same type:

- Last write wins.
- Log a warning (optional).

---

# 🌿 **7. Thread Safety**
Registry operations must be thread-safe.

Implementation options:

- Immutable registry with atomic swap  
- ConcurrentDictionary  
- Lock around mutation  

Recommendation: **immutable registry + atomic swap** for determinism.

---

# 🌳 **8. Fable Compatibility**
The registry must:

- avoid reflection  
- avoid Type.GetType  
- avoid runtime type scanning  
- avoid dynamic code generation  

All registration is explicit.

---

# 🌄 **9. Deliverables for Claude**

### **Claude should implement:**

- `CodecRegistry` type  
- `CodecRegistry` module  
- `GlobalCodecRegistry` module  
- `IJsonCodec` + `JsonCodec<'T>`  
- primitive codecs  
- codec adapter  
- lookup logic  
- integration hooks (but not generator logic)  

This is a pure F# module — no MSBuild, no Djinn, no generators.

---
