## **Specification: Enforce Strict Serde Metadata on Nested Types**

### **Goal**
Ensure that **all user-defined types** participating in serialization or deserialization must have Serde metadata. If a nested type lacks Serde metadata, the generator must emit a **strict violation** at metadata generation time. No fallback, no structural inference, no silent behavior.

This must apply recursively.

---

## **Scope**
This change affects:

- `TypeKindExtractor`
- `AstParser`
- `SerdeAstParser`
- `SerdeTypeInfo` construction
- The design-time emitter (only where it currently generates structural serializers)
- The generator pipeline that walks nested types

This change does **not** affect:

- The runtime backend (`Serde.FS.Json`)
- The public API surface
- Attribute definitions
- Existing DU/record encoding rules

---

## **Invariants (MUST be enforced)**

### **1. Every user-defined type must have Serde metadata**
A type is considered “user-defined” if it is:

- a record  
- a DU  
- a struct  
- a class with fields/properties  

If such a type lacks a Serde attribute, this is a **strict violation**.

### **2. Primitive and built-in types are always allowed**
These do **not** require Serde attributes:

- `int`, `string`, `float`, `bool`, etc.  
- `option<'T>`  
- `list<'T>`  
- `array<'T>`  
- `Set<'T>`  
- `Map<'K,'V>`  
- tuples  
- anonymous records  

### **3. Nested types must be validated recursively**
If `Person` has a field of type `Address`, and `Address` has a field of type `ZipCode`, then:

- `Person` must have Serde metadata  
- `Address` must have Serde metadata  
- `ZipCode` must have Serde metadata  

If any of these are missing, the generator must fail.

### **4. No structural inference**
The generator must **not** generate serializers for:

- records without Serde attributes  
- DUs without Serde attributes  
- classes without Serde attributes  

### **5. Errors must occur at metadata generation time**
Not at runtime.  
Not during serialization.  
Not during deserialization.

---

## **Required Behavior**

### **When encountering a nested type without Serde metadata:**
The generator must produce a compile-time error:

```
Serde error: Type '<FullName>' is used in serialization but does not have Serde metadata. 
Add [<Serde>] to the type definition.
```

### **When encountering a type that is allowed structurally (tuple, option, list, etc.):**
Continue recursively validating element types.

### **When encountering a primitive:**
Accept immediately.

---

## **Implementation Tasks**

### **Task 1 — Add recursive metadata validation**
Modify the type-walking logic so that:

- When a field/case payload type is encountered:
  - If primitive → OK  
  - If collection → recurse into element type  
  - If tuple → recurse into each element  
  - If anonymous record → recurse into each field  
  - If user-defined type:
    - Check if Serde metadata exists  
    - If not → emit strict violation  
    - If yes → recurse into its fields/cases  

### **Task 2 — Update `SerdeTypeInfo` construction**
Ensure that `SerdeTypeInfo` is only constructed for types with Serde metadata.

### **Task 3 — Remove structural serializer generation**
Delete or disable any code paths that:

- generate serializers for records without Serde attributes  
- generate serializers for DUs without Serde attributes  

### **Task 4 — Add tests**
Add tests verifying:

- A nested record without `[<Serde>]` causes a strict violation  
- A nested DU without `[<Serde>]` causes a strict violation  
- Deeply nested types fail correctly  
- Tuples, options, lists, arrays, sets, maps still work  
- Anonymous records still work  
- Primitive-only types still work  

### **Task 5 — Do not modify runtime backend**
The runtime backend must remain unchanged.

---

## **Do Not Touch**
Claude must not modify:

- `Serde.FS.Json` runtime backend  
- DU encoding rules  
- naming rules  
- attribute definitions  
- buildTransitive targets  
- version propagation logic  
- project structure  

---

## **Acceptance Criteria**

- Serializing a type with a nested record lacking `[<Serde>]` produces a compile-time error.
- Serializing a type with a nested DU lacking `[<Serde>]` produces a compile-time error.
- No structural serializers are generated for user-defined types.
- All existing tests pass except those expecting fallback behavior (which must be updated).
- New strictness tests pass.
- SampleApp now throws a strict violation when `Name` lacks `[<Serde>]`.

---

If you want, I can also generate the **exact prompt wording** you can paste into Claude so it executes this spec without drifting.