namespace Serde.FS.SourceGen

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Text
open System.Text

[<Generator>]
type SerdeSourceGenerator() =
    interface ISourceGenerator with
        member _.Initialize(context: GeneratorInitializationContext) =
            ()

        member _.Execute(context: GeneratorExecutionContext) =
            let emitter =
                match Serde.FS.SerdeCodegenRegistry.getDefaultEmitter() with
                | Some e -> e
                | None -> failwith "No Serde code emitter registered. Call SerdeCodegenRegistry.setDefaultEmitter() before running the generator."

            let sourceFiles =
                context.AdditionalFiles
                |> Seq.choose (fun file ->
                    let text = file.GetText(context.CancellationToken)
                    match text with
                    | null -> None
                    | t -> Some (file.Path, t.ToString()))
                |> Seq.toList

            let result = SerdeGeneratorEngine.generate sourceFiles emitter

            for warning in result.Warnings do
                let diag = Diagnostic.Create(
                    DiagnosticDescriptor("SERDE001", "Serde Warning", warning, "Serde.FS", DiagnosticSeverity.Warning, true),
                    Location.None)
                context.ReportDiagnostic(diag)

            for error in result.Errors do
                let diag = Diagnostic.Create(
                    DiagnosticDescriptor("SERDE002", "Serde Error", error, "Serde.FS", DiagnosticSeverity.Error, true),
                    Location.None)
                context.ReportDiagnostic(diag)

            for source in result.Sources do
                context.AddSource(source.HintName, SourceText.From(source.Code, Encoding.UTF8))
