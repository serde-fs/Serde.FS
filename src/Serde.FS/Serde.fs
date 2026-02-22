namespace Serde.FS

type Serde =
    static member val DefaultBackend : ISerdeBackend option = None
        with get, set

    /// When true, serialization/deserialization of types without generated metadata throws.
    static member val Strict : bool = false
        with get, set

    /// When true, enables debug logging for Serde operations.
    static member val Debug : bool = false
        with get, set

    static member GetBackend() =
        Serde.DefaultBackend
        |> Option.defaultWith (fun () ->
            failwith "No backend registered. Reference a backend package such as Serde.FS.STJ."
        )

    static member inline Serialize(value: 'T) =
        Serde.GetBackend().Serialize(value, typeof<'T>, None)

    static member inline Serialize(value: 'T, options: ISerdeOptions) =
        Serde.GetBackend().Serialize(value, typeof<'T>, Some options)

    static member inline Deserialize<'T>(json: string) =
        Serde.GetBackend().Deserialize<'T>(json, typeof<'T>, None)

    static member inline Deserialize<'T>(json: string, options: ISerdeOptions) =
        Serde.GetBackend().Deserialize<'T>(json, typeof<'T>, Some options)
