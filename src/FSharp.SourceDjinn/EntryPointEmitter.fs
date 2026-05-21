namespace FSharp.SourceDjinn

module EntryPointEmitter =

    let emit (info: EntryPointInfo) : string =
        "namespace FSharp.SourceDjinn.Generated\n" +
        "\n" +
        "module internal DjinnBootstrap =\n" +
        "\n" +
        "    let runBootstraps () =\n" +
        "        try\n" +
        "            // Ensure referenced assemblies are loaded\n" +
        "            let entry = System.Reflection.Assembly.GetEntryAssembly()\n" +
        "            if not (isNull entry) then\n" +
        "                for name in entry.GetReferencedAssemblies() do\n" +
        "                    try System.Reflection.Assembly.Load(name) |> ignore with _ -> ()\n" +
        "\n" +
        "            // Discover and run all IEntryPointBootstrap implementors\n" +
        "            for asm in System.AppDomain.CurrentDomain.GetAssemblies() do\n" +
        "                let types =\n" +
        "                    try asm.GetTypes()\n" +
        "                    with :? System.Reflection.ReflectionTypeLoadException as ex ->\n" +
        "                        ex.Types |> Array.filter (fun t -> not (isNull t))\n" +
        "                for ty in types do\n" +
        sprintf "                    if typeof<%s>.IsAssignableFrom(ty)\n" info.BootstrapInterface +
        "                       && not ty.IsInterface\n" +
        "                       && not ty.IsAbstract then\n" +
        sprintf "                        let instance = System.Activator.CreateInstance(ty) :?> %s\n" info.BootstrapInterface +
        "                        instance.Init()\n" +
        "        with _ -> ()\n" +
        "\n" +
        sprintf "module DjinnEntryPoint =\n\n    [<EntryPoint>]\n    let main argv =\n        DjinnBootstrap.runBootstraps ()\n        %s.%s argv\n"
            info.ModuleName info.FunctionName
