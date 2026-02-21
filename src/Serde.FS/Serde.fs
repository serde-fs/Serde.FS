namespace Serde.FS

type Serde =
    static member val DefaultBackend : ISerdeBackend option = None
        with get, set

    static member private GetBackend() =
        Serde.DefaultBackend
        |> Option.defaultWith (fun () ->
            failwith "No backend registered. Reference a backend package such as Serde.FS.STJ."
        )

    static member Serialize(value: 'T) =
        Serde.GetBackend().Serialize(value, None)

    static member Serialize(value: 'T, options: ISerdeOptions) =
        Serde.GetBackend().Serialize(value, Some options)

    static member Deserialize<'T>(json: string) =
        Serde.GetBackend().Deserialize<'T>(json, None)

    static member Deserialize<'T>(json: string, options: ISerdeOptions) =
        Serde.GetBackend().Deserialize<'T>(json, Some options)
