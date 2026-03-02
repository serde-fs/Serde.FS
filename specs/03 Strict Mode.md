# ⭐ **Claude Spec — Strict Mode (Core + STJ Backend Implementation)**  
Strict mode is a **Serde‑level invariant** implemented by all backends.  
STJ is the first backend to enforce it.

---

# 1. **Move Strict Mode into the Core Interface**

### **Update the core options interface:**

```fsharp
type ISerdeOptions =
    abstract Strict : bool with get, set
```

### **Behavior:**
- `Strict = true` is the **default** for all backends.
- Backends may add additional strictness rules, but the presence of strict mode is universal.

---

# 2. **Update STJ Backend Options to Implement ISerdeOptions**

### **Replace the old STJ‑specific options with:**

```fsharp
type SerdeJsonOptions =
    { mutable Strict : bool
      // STJ-specific knobs may be added here later
    }
    interface ISerdeOptions with
        member x.Strict
            with get() = x.Strict
            and set v = x.Strict <- v
```

### **Add a module‑level instance with strict ON by default:**

```fsharp
module SerdeJson =
    let options =
        { Strict = true }
```

### **Add configuration helpers:**

```fsharp
module SerdeJson =
    let configure (f : SerdeJsonOptions -> unit) =
        f options

    let allowReflectionFallback () =
        options.Strict <- false
```

---

# 3. **Modify STJ Serialize/Deserialize to Enforce Strict Mode**

### **Where STJ currently resolves type info:**

```fsharp
let typeInfo = options.GetTypeInfo(typeof<'T>)
```

### **Insert strict‑mode check:**

```fsharp
if options.Strict && typeInfo = null then
    failwith $"No generated serializer found for type {typeof<'T>.FullName}. Strict mode is enabled."
```

### **Apply the same check to Deserialize.**

### **Constraints:**
- Do NOT modify the generator.
- Do NOT modify the core beyond adding `Strict` to `ISerdeOptions`.
- Enforcement logic lives **only** in the STJ backend.

---

# 4. **Strict Mode Behavior Rules**

### **If strict mode is ON (default):**
- Missing metadata → throw
- Missing converter → throw
- Unmarked type → throw
- Partially generated type → throw

### **If strict mode is OFF:**
- STJ’s reflection fallback is allowed
- No additional behavior is required

---

# 5. **Tests**

### **Strict mode ON (default):**
- Serializing a type without generated metadata → throws
- Deserializing a type without generated metadata → throws
- Serializing a generated type → succeeds

### **Strict mode OFF:**
- `SerdeJson.allowReflectionFallback()` enables reflection fallback
- Serializing unmarked types works via STJ reflection

---

# 6. **Acceptance Criteria**

- Strict mode is ON by default for all backends via `ISerdeOptions`.
- STJ backend implements `ISerdeOptions` and enforces strictness.
- Users must explicitly disable strictness.
- STJ backend throws when metadata is missing and strict mode is enabled.
- Reflection fallback only occurs when strict mode is disabled.
- No generator changes.
- All tests pass.

---