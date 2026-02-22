namespace Serde.FS

module SerdeApp =
    let mutable private _entryPoint : (string[] -> int) option = None

    /// Marks this function as the application's entry point.
    ///
    /// Serde.FS will generate the actual `[<EntryPoint>]` function that calls it.
    let entryPoint fn =
        _entryPoint <- Some fn

    let invokeRegisteredEntryPoint argv =
        match _entryPoint with
        | Some fn -> fn argv
        | None -> 0
