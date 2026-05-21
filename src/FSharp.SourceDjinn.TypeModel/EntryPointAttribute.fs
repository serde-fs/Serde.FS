namespace FSharp.SourceDjinn.TypeModel

[<System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)>]
type EntryPointAttribute() =
    inherit System.Attribute()
