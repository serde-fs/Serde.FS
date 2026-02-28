namespace FSharp.SourceDjinn

module EntryPointEmitter =

    let emit (info: EntryPointInfo) : string =
        "namespace FSharp.SourceDjinn.Generated\n" +
        "\n" +
        "module internal DjinnBootstrap =\n" +
        "    let mutable private conventionBootstrapWasCalled = false\n" +
        "\n" +
        "    let tryConventionBootstrap () =\n" +
        "        try\n" +
        "            let asm = System.Reflection.Assembly.GetEntryAssembly()\n" +
        "            if not (isNull asm) then\n" +
        "                match asm.GetType(\"Djinn.Generated.Bootstrap\") with\n" +
        "                | null -> ()\n" +
        "                | ty ->\n" +
        "                    let m =\n" +
        "                        ty.GetMethod(\n" +
        "                            \"init\",\n" +
        "                            System.Reflection.BindingFlags.Public\n" +
        "                            ||| System.Reflection.BindingFlags.Static)\n" +
        "                    if not (isNull m) && m.GetParameters().Length = 0 then\n" +
        "                        m.Invoke(null, [||]) |> ignore\n" +
        "                        conventionBootstrapWasCalled <- true\n" +
        "        with _ -> ()\n" +
        "\n" +
        "    let fallbackToReflectionBootstrap () =\n" +
        "        if conventionBootstrapWasCalled then ()\n" +
        "        else\n" +
        "            // Minimal stub — no Djinn-owned metadata to activate yet.\n" +
        "            // Can be extended later for Djinn-scoped registrations.\n" +
        "            ()\n" +
        "\n" +
        sprintf "module DjinnEntryPoint =\n\n    [<EntryPoint>]\n    let main argv =\n        DjinnBootstrap.tryConventionBootstrap ()\n        DjinnBootstrap.fallbackToReflectionBootstrap ()\n        %s.%s argv\n"
            info.ModuleName info.FunctionName
