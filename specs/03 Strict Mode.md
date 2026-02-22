# ⭐ **Claude Spec — Add Strict Mode to STJ Backend (Strict by Default)**

This spec assumes the codegen inversion is underway or complete.  
It is backend‑only and does not affect the generator or the core.

---

# 1. **Add strict mode configuration to Serde.FS.STJ**

### **Create a configuration record:**

```fsharp
type SerdeStjOptions =
    { mutable Strict : bool }
```

### **Add a module‑level instance with strict ON by default:**

```fsharp
module SerdeStj =
    let options = SerdeStjOptions(Strict = true)
```

### **Add configuration helpers:**

```fsharp
module SerdeStj =
    let configure (f : SerdeStjOptions -> unit) =
        f options

    let allowReflectionFallback () =
        options.Strict <- false
```

### **Behavior:**
- `Strict = true` is the default.
- Users must explicitly disable strictness.

---

# 2. **Modify STJ backend Serialize/Deserialize to enforce strict mode**

### **Where STJ currently resolves type info:**

```fsharp
let typeInfo = options.GetTypeInfo(typeof<'T>)
```

### **Insert strict‑mode check:**

```fsharp
if SerdeStj.options.Strict && typeInfo = null then
    failwith $"No generated serializer found for type {typeof<'T>.FullName}. Strict mode is enabled."
```

### **Apply the same check to Deserialize.**

### **Constraints:**
- Do NOT modify the generator.
- Do NOT modify the core.
- This logic belongs only in the STJ backend.

---

# 3. **Do NOT allow silent fallback when strict mode is enabled**

### **If strict mode is ON:**
- Missing metadata → throw
- Missing converter → throw
- Unmarked type → throw
- Partially generated type → throw

### **If strict mode is OFF:**
- STJ’s reflection fallback is allowed
- No additional behavior is required

---

# 4. **Add tests**

### **Strict mode ON (default):**
- Serializing a type without generated metadata → throws
- Deserializing a type without generated metadata → throws
- Serializing a generated type → succeeds

### **Strict mode OFF:**
- `SerdeStj.allowReflectionFallback()` enables reflection fallback
- Serializing unmarked types works via STJ reflection

---

# 5. **Acceptance Criteria**

- Strict mode is ON by default.
- Users must explicitly disable strictness.
- STJ backend throws when metadata is missing and strict mode is enabled.
- Reflection fallback only occurs when strict mode is disabled.
- No changes to generator or core.
- All tests pass.

---
