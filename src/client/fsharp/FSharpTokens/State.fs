module FantomasTools.Client.FSharpTokens.State

open System
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Fetch
open Thoth.Json
open Browser
open Browser.Types
open FantomasTools.Client.FSharpTokens.Model
open FantomasTools.Client.FSharpTokens.Decoders
open FantomasTools.Client.FSharpTokens.Encoders
open FantomasTools.Client

[<Emit("import.meta.env.VITE_FSHARP_TOKENS_BACKEND")>]
let private backend: string = jsNative

let private getTokens (request: FSharpTokens.Shared.GetTokensRequest) : JS.Promise<string> =
    let url = sprintf "%s/%s" backend "api/get-tokens"

    let json = Encode.toString 4 (encodeGetTokensRequest request)

    fetch url [ Body(!^json); Method HttpMethod.POST ]
    |> Promise.bind (fun res -> res.text ())

let private getVersion () =
    sprintf "%s/%s" backend "api/version"
    |> Http.getText

let private initialModel =
    { Defines = ""
      Tokens = [||]
      ActiveLine = None
      ActiveTokenIndex = None
      IsLoading = false
      Version = "??" }

let private decodeGetTokensRequest: Decoder<FSharpTokens.Shared.GetTokensRequest> =
    Decode.object (fun get ->
        { Defines = get.Required.Field "defines" (Decode.list Decode.string)
          SourceCode = get.Required.Field "sourceCode" Decode.string })

let private splitDefines (value: string) =
    value.Split([| ' '; ';' |], StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

let private nodeListToArray (nl: NodeListOf<Element>) : Element array =
    emitJsStatement nl "Array.prototype.slice.call($0)"

let private dollar (selector: string) : NodeListOf<Element> = document.querySelectorAll (selector)

let private scrollIntoView (element: Element) : unit =
    emitJsStatement
        element
        """
$0.scrollIntoView({
  behavior: "smooth",
  block: "end",
  inline: "center"
});
        """

let rec private scrollToImpl (index: int) (attempt: int) : unit =
    if attempt < 5 then
        let details = dollar "#details .detail"
        let elem = details.[index]

        if not (isNullOrUndefined elem) then
            scrollIntoView elem
            elem.classList.add "lit"

            window.setTimeout ((fun () -> elem.classList.remove "lit"), 1000)
            |> ignore
        else
            window.setTimeout ((fun () -> scrollToImpl index (attempt + 1)), 150)
            |> ignore

let scrollTo (index: int) : unit = scrollToImpl index 0

let private modelToParseRequest sourceCode (model: Model) : FSharpTokens.Shared.GetTokensRequest =
    let defines = splitDefines model.Defines

    { Defines = defines
      SourceCode = sourceCode }

let private updateUrl code (model: Model) _ =
    let json = Encode.toString 2 (encodeUrlModel code model)

    UrlTools.updateUrlWithData json

let init isActive =
    let model =
        if isActive then
            UrlTools.restoreModelFromUrl (decodeUrlModel initialModel) initialModel
        else
            initialModel

    let cmd =
        Cmd.OfPromise.either getVersion () VersionFound NetworkException

    model, cmd

let update code msg model =
    match msg with
    | GetTokens ->
        let cmd =
            let requestCmd =
                Cmd.OfPromise.perform getTokens (modelToParseRequest code model) TokenReceived

            let updateUrlCmd = Cmd.ofSub (updateUrl code model)
            Cmd.batch [ requestCmd; updateUrlCmd ]

        { model with IsLoading = true }, cmd
    | TokenReceived (tokensText) ->
        match decodeTokens tokensText with
        | Ok tokens ->
            let cmd =
                if (Array.length tokens) = 1 then
                    Cmd.OfFunc.result (LineSelected 1)
                else
                    Cmd.none

            { model with
                Tokens = tokens
                IsLoading = false },
            cmd
        | Error error ->
            printfn "%A" error
            { model with IsLoading = false }, Cmd.none
    | LineSelected lineNumber -> { model with ActiveLine = Some lineNumber }, Cmd.none

    | TokenSelected tokenIndex ->
        let highlightCmd =
            model.ActiveLine
            |> Option.map (fun activeLine ->
                let token =
                    model.Tokens
                    |> Array.filter (fun t -> t.LineNumber = activeLine)
                    |> Array.item tokenIndex
                    |> fun t -> t.TokenInfo

                let range: Editor.HighLightRange =
                    { StartLine = activeLine
                      StartColumn = token.LeftColumn
                      EndLine = activeLine
                      EndColumn = token.RightColumn + 1 }

                Cmd.ofSub (Editor.selectRange range))
            |> Option.defaultValue Cmd.none

        let scrollCmd = Cmd.OfFunc.result (PlayScroll tokenIndex)

        { model with ActiveTokenIndex = Some tokenIndex }, Cmd.batch [ highlightCmd; scrollCmd ]
    | PlayScroll index -> model, Cmd.ofSub (fun _ -> scrollTo index)
    | DefinesUpdated defines -> { model with Defines = defines }, Cmd.none
    | VersionFound v -> { model with Version = v }, Cmd.none
    | NetworkException e ->
        JS.console.error e
        model, Cmd.none
