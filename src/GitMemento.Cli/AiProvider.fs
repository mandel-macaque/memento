namespace GitMemento

open System
open System.Text.Json
open System.Threading.Tasks

type ProviderSettings =
    { Provider: string
      Executable: string
      GetArgs: string
      ListArgs: string }

type IAiSessionProvider =
    abstract member Name: string
    abstract member GetSessionAsync: sessionId: string -> Task<Result<SessionData, string>>
    abstract member ListSessionsAsync: unit -> Task<Result<SessionRef list, string>>

module private SessionJson =
    let tryGetProperty (name: string) (element: JsonElement) =
        let mutable value = Unchecked.defaultof<JsonElement>
        if element.TryGetProperty(name, &value) then Some value else None

    let parseRole (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "user" -> MessageRole.User
        | "assistant" -> MessageRole.Assistant
        | "system" -> MessageRole.System
        | "tool" -> MessageRole.Tool
        | _ -> MessageRole.Assistant

    let rec extractTextFromContent (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String -> element.GetString() |> Option.ofObj |> Option.defaultValue String.Empty
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.map extractTextFromContent
            |> String.concat Environment.NewLine
        | JsonValueKind.Object ->
            match tryGetProperty "text" element with
            | Some text -> extractTextFromContent text
            | None ->
                match tryGetProperty "content" element with
                | Some content -> extractTextFromContent content
                | None -> String.Empty
        | _ -> String.Empty

    let parseMessages (root: JsonElement) =
        let collection =
            if root.ValueKind = JsonValueKind.Array then
                root.EnumerateArray() |> Seq.toList
            else
                match tryGetProperty "messages" root with
                | Some messages when messages.ValueKind = JsonValueKind.Array -> messages.EnumerateArray() |> Seq.toList
                | _ ->
                    match tryGetProperty "items" root with
                    | Some items when items.ValueKind = JsonValueKind.Array -> items.EnumerateArray() |> Seq.toList
                    | _ -> List.empty

        collection
        |> List.map (fun message ->
            let role =
                tryGetProperty "role" message
                |> Option.map (fun p -> p.GetString() |> Option.ofObj |> Option.defaultValue "assistant")
                |> Option.defaultValue "assistant"
                |> parseRole

            let content =
                match tryGetProperty "content" message with
                | Some data -> extractTextFromContent data
                | None ->
                    match tryGetProperty "text" message with
                    | Some data -> extractTextFromContent data
                    | None -> String.Empty

            { Role = role
              Content = content
              Timestamp = None })

    let parseSession (providerName: string) (sessionId: string) (json: string) : Result<SessionData, string> =
        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let resolvedId =
                tryGetProperty "id" root
                |> Option.map (fun p -> p.GetString() |> Option.ofObj |> Option.defaultValue sessionId)
                |> Option.defaultValue sessionId

            let title =
                tryGetProperty "title" root
                |> Option.map (fun p -> p.GetString())
                |> Option.bind Option.ofObj
                |> Option.map (fun value -> value.Trim())
                |> Option.filter (String.IsNullOrWhiteSpace >> not)

            Ok
                { Id = resolvedId
                  Provider = providerName
                  Title = title
                  Messages = parseMessages root }
        with ex ->
            Error $"Unable to parse {providerName} session JSON: {ex.Message}"

    let parseSessionRefs (providerName: string) (json: string) =
        let parseElement (element: JsonElement) =
            let id =
                tryGetProperty "id" element
                |> Option.map (fun p -> p.GetString())
                |> Option.bind Option.ofObj
                |> Option.map (fun value -> value.Trim())
                |> Option.filter (String.IsNullOrWhiteSpace >> not)

            let title =
                tryGetProperty "title" element
                |> Option.map (fun p -> p.GetString())
                |> Option.bind Option.ofObj
                |> Option.map (fun value -> value.Trim())
                |> Option.filter (String.IsNullOrWhiteSpace >> not)

            id |> Option.map (fun foundId -> { Id = foundId; Title = title })

        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let elements =
                if root.ValueKind = JsonValueKind.Array then
                    root.EnumerateArray() |> Seq.toList
                else
                    match tryGetProperty "sessions" root with
                    | Some sessions when sessions.ValueKind = JsonValueKind.Array -> sessions.EnumerateArray() |> Seq.toList
                    | _ -> List.empty

            elements |> List.choose parseElement |> Ok
        with ex ->
            Error $"Unable to parse {providerName} session list JSON: {ex.Message}"

type CliJsonProvider(providerName: string, settings: ProviderSettings, runner: ICommandRunner) =
    interface IAiSessionProvider with
        member _.Name = providerName

        member _.GetSessionAsync(sessionId: string) =
            task {
                let args =
                    settings.GetArgs.Replace("{id}", sessionId)
                    |> CommandLine.splitArgs
                let! result = runner.RunCaptureAsync(settings.Executable, args)
                if result.ExitCode <> 0 then
                    return
                        Error(
                            if String.IsNullOrWhiteSpace result.StdErr then
                                result.StdOut
                            else
                                result.StdErr
                        )
                else
                    return SessionJson.parseSession providerName sessionId result.StdOut
            }

        member _.ListSessionsAsync() =
            task {
                let args = CommandLine.splitArgs settings.ListArgs
                let! result = runner.RunCaptureAsync(settings.Executable, args)
                if result.ExitCode <> 0 then
                    return
                        Error(
                            if String.IsNullOrWhiteSpace result.StdErr then
                                result.StdOut
                            else
                                result.StdErr
                        )
                else
                    return SessionJson.parseSessionRefs providerName result.StdOut
            }

module AiProviderFactory =
    let private envOrDefault key fallback =
        Environment.GetEnvironmentVariable(key)
        |> function
            | null
            | "" -> fallback
            | value -> value

    let normalizeProvider (value: string) =
        let normalized = value.Trim().ToLowerInvariant()
        match normalized with
        | "codex" -> Ok "codex"
        | "claude"
        | "claude-code" -> Ok "claude"
        | _ -> Error $"Unsupported provider '{value}'. Supported: codex, claude."

    let defaultSettings (provider: string) =
        match normalizeProvider provider with
        | Error err -> Error err
        | Ok "codex" ->
            Ok
                { Provider = "codex"
                  Executable = envOrDefault "MEMENTO_CODEX_BIN" "codex"
                  GetArgs = envOrDefault "MEMENTO_CODEX_GET_ARGS" "sessions get {id} --json"
                  ListArgs = envOrDefault "MEMENTO_CODEX_LIST_ARGS" "sessions list --json" }
        | Ok "claude" ->
            Ok
                { Provider = "claude"
                  Executable = envOrDefault "MEMENTO_CLAUDE_BIN" "claude"
                  GetArgs = envOrDefault "MEMENTO_CLAUDE_GET_ARGS" "sessions get {id} --json"
                  ListArgs = envOrDefault "MEMENTO_CLAUDE_LIST_ARGS" "sessions list --json" }
        | _ -> Error $"Unsupported provider '{provider}'."

    let createFromSettings (runner: ICommandRunner) (settings: ProviderSettings) : IAiSessionProvider =
        match settings.Provider with
        | "codex" -> CliJsonProvider("Codex", settings, runner) :> IAiSessionProvider
        | "claude" -> CliJsonProvider("Claude", settings, runner) :> IAiSessionProvider
        | unknown -> failwith $"Unsupported AI provider: {unknown}"
