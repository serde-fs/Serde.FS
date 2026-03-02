Plan: Project-Scoped Resolver System (Spec 20)

Context

The generated entry point currently forces ALL module initializers via Assembly.GetTypes() + RunClassConstructor() to ensure the resolver's
do binding runs. This is a reflection sweep — fragile, not AOT-safe, and leaks Serde-specific behavior into what should be a generic entry
point.

The fix: replace the module-level do registration with explicit register() functions, a project-wide registerAll(), and a callback hook so
useAsDefault() triggers registration without reflection.

---
Step 1: Add callback slot to Json

New file: src/Serde.FS.Json/ResolverBootstrap.fs

namespace Serde

module ResolverBootstrap =
    let mutable registerAll : (unit -> unit) option = None

Edit: src/Serde.FS.Json/Serde.FS.Json.fsproj
- Add <Compile Include="ResolverBootstrap.fs" /> BEFORE JsonBackendOptions.fs (so it's available to later files)

---
Step 2: Invoke callback from useAsDefault()

Edit: src/Serde.FS.Json/SerdeJson.fs

Add callback invocation at the top of useAsDefault():

let useAsDefault () =
    match global.Serde.ResolverBootstrap.registerAll with
    | Some f -> f()
    | None -> ()
    match Serde.DefaultBackend with
    // ... rest unchanged

Note: global.Serde.ResolverBootstrap disambiguates the Serde namespace from the Serde.FS.Serde type visible in this file.

---
Step 3: Change resolver emitter — do → register()

Edit: src/Serde.FS.Json/designTime/JsonCodeEmitter.fs — emitResolver method (line 478)

Replace:
append "do Serde.FS.Json.SerdeJsonResolverRegistry.registerResolver(SerdeJsonGeneratedResolver())"

With:
append "let register() ="
append "    Serde.FS.Json.SerdeJsonResolverRegistry.registerResolver(SerdeJsonGeneratedResolver())"

No other changes to the resolver emitter. The resolver type and GetTypeInfo dispatch remain the same.

---
Step 4: Generate registration + bootstrap file

Edit: src/Serde.FS.SourceGen/SerdeGeneratorTask.fs

After the existing resolver emit block (lines 243-258), add generation of a combined registration/bootstrap file
~SerdeResolverRegistration.djinn.g.fs:

namespace Serde.Generated

module ResolverRegistration =
    let mutable private initialized = false
    let registerAll() =
        if not initialized then
            initialized <- true
            SerdeJsonResolver.register()

module internal ResolverBootstrap =
    [<System.Runtime.CompilerServices.ModuleInitializer>]
    let init() =
        Serde.ResolverBootstrap.registerAll <- Some ResolverRegistration.registerAll

Key details:
- registerAll() is idempotent (safe to call multiple times)
- [<ModuleInitializer>] ensures the callback is set at assembly load, before any user code
- File name ~SerdeResolverRegistration.djinn.g.fs sorts after ~SerdeResolver.serde.g.fs (because at position 14, R > . in ASCII)
- The [<ModuleInitializer>] is the ONLY module-init side effect — it just sets a callback pointer, doesn't register resolvers

---
Step 5: Revert entry point to use generic EntryPointEmitter.emit

Edit: src/Serde.FS.SourceGen/SerdeGeneratorTask.fs (lines 260-290)

Replace the Serde-specific entry point generation (with reflection) with a call to EntryPointEmitter.emit:

match EntryPointDetector.detect filePath sourceText with
| Some info ->
    let code = EntryPointEmitter.emit info
    let outputFile = Path.Combine(this.OutputDir, "~~EntryPoint.djinn.g.fs")
    // ... same write-if-changed logic

The generated entry point becomes purely generic — just calls the user's function. No reflection, no Serde logic.

---
File compilation order (alphabetical glob)

1. Address.serde.g.fs, Color.serde.g.fs, ... (per-type converters)
2. ~SerdeResolver.serde.g.fs (resolver type + register())
3. ~SerdeResolverRegistration.djinn.g.fs (registerAll + bootstrap)
4. ~~EntryPoint.djinn.g.fs (generic entry point, last)

---
Runtime flow

Assembly loads
→ [ModuleInitializer] fires: sets Serde.ResolverBootstrap.registerAll callback
→ ~~EntryPoint.djinn.g.fs: DjinnEntryPoint.main argv
    → Program.run argv
    → SerdeJson.useAsDefault()
        → invokes callback → ResolverRegistration.registerAll()
        → SerdeJsonResolver.register()
            → SerdeJsonResolverRegistry.registerResolver(...)
        → Serde.Strict <- true
    → Serde.Serialize person  // strict check passes
