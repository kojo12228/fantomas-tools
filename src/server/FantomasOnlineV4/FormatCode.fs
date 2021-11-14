module FantomasOnlineV4.FormatCode

open Fantomas
open Fantomas.FormatConfig
open Fantomas.Extras
open FantomasOnline.Shared
open FantomasOnline.Server.Shared.Http
open FSharp.Compiler.SourceCodeServices

let private checker = FakeHelpers.sharedChecker.Force()

let private mapFantomasOptionsToRecord options =
    let newValues =
        options
        |> Seq.map (function
            | BoolOption (_, _, v) -> box v
            | IntOption (_, _, v) -> box v
            | MultilineFormatterTypeOption (_, _, v) ->
                MultilineFormatterType.OfConfigString(v)
                |> Option.defaultValue (box CharacterWidth)
            | EndOfLineStyleOption (_, _, v) ->
                EndOfLineStyle.OfConfigString(v)
                |> Option.defaultValue EndOfLineStyle.CRLF
                |> box)
        |> Seq.toArray

    let formatConfigType = typeof<FormatConfig.FormatConfig>
    Microsoft.FSharp.Reflection.FSharpValue.MakeRecord(formatConfigType, newValues) :?> FormatConfig.FormatConfig

let private format fileName code config =
    let options = FakeHelpers.createParsingOptionsFromFile fileName

    let source = SourceOrigin.SourceString code
    CodeFormatter.FormatDocumentAsync(fileName, source, config, options, checker)

let private validate fileName code =
    let options =
        { FSharpParsingOptions.Default with SourceFiles = [| fileName |] }

    let sourceCode = FSharp.Compiler.Text.SourceText.ofString code

    async {
        let! result = checker.ParseFile(fileName, sourceCode, options)

        return
            result.Errors
            |> Array.map (fun e ->
                { SubCategory = e.Subcategory
                  Range =
                    { StartLine = e.Range.StartLine
                      StartCol = e.Range.StartColumn
                      EndLine = e.Range.EndLine
                      EndCol = e.Range.EndColumn }
                  Severity =
                    match e.Severity with
                    | FSharpDiagnosticSeverity.Warning -> ASTErrorSeverity.Warning
                    | FSharpDiagnosticSeverity.Error -> ASTErrorSeverity.Error
                    | FSharpDiagnosticSeverity.Hidden -> ASTErrorSeverity.Hidden
                    | FSharpDiagnosticSeverity.Info -> ASTErrorSeverity.Info
                  ErrorNumber = e.ErrorNumber
                  Message = e.Message })
            |> Array.toList
    }

let getVersion () =
    let assembly = typeof<FormatConfig.FormatConfig>.Assembly

    let version = assembly.GetName().Version
    sprintf "%i.%i.%i" version.Major version.Minor version.Build

let getOptions () : string =
    Reflection.getRecordFields FormatConfig.FormatConfig.Default
    |> Seq.indexed
    |> Seq.choose (fun (idx, (k: string, v: obj)) ->
        match v with
        | :? int as i -> FantomasOption.IntOption(idx, k, i) |> Some
        | :? bool as b -> FantomasOption.BoolOption(idx, k, b) |> Some
        | _ -> None)
    |> Seq.toList
    |> mapOptionsToJson

let formatCode: string -> Async<FormatResponse> =
    formatCode mapFantomasOptionsToRecord format validate
