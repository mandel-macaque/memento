namespace GitMemento

open System
open System.Text

module Markdown =
    let private roleName (committerAlias: string) (provider: string) (role: MessageRole) =
        match role with
        | MessageRole.User -> committerAlias
        | MessageRole.Assistant -> provider
        | MessageRole.System -> "System"
        | MessageRole.Tool -> "Tool"

    let private truncate (maxLength: int) (value: string) =
        if String.IsNullOrEmpty value || value.Length <= maxLength then
            value
        else
            value.AsSpan(0, maxLength).ToString() + "..."

    let renderConversation (committerAlias: string) (session: SessionData) =
        let sb = StringBuilder(2048)
        sb.AppendLine("# Git Memento Session") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine($"- Provider: {session.Provider}") |> ignore
        sb.AppendLine($"- Session ID: {session.Id}") |> ignore

        match session.Title with
        | Some title when not (String.IsNullOrWhiteSpace title) -> sb.AppendLine($"- Session Title: {title}") |> ignore
        | _ -> ()

        sb.AppendLine($"- Committer: {committerAlias}") |> ignore
        sb.AppendLine($"- Captured At (UTC): {DateTimeOffset.UtcNow:O}") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("## Conversation") |> ignore
        sb.AppendLine() |> ignore

        if List.isEmpty session.Messages then
            sb.AppendLine("_No conversation messages were found for this session._") |> ignore
        else
            for message in session.Messages do
                let speaker = roleName committerAlias session.Provider message.Role
                let content = TextCleaning.cleanBlock message.Content
                sb.AppendLine($"### {speaker}") |> ignore
                sb.AppendLine() |> ignore
                if String.IsNullOrWhiteSpace content then
                    sb.AppendLine("_No content_") |> ignore
                else
                    sb.AppendLine(content) |> ignore
                sb.AppendLine() |> ignore

        sb.ToString().Trim() + Environment.NewLine

    let buildSummary (session: SessionData) =
        session.Messages
        |> List.map (fun message ->
            let speaker =
                match message.Role with
                | MessageRole.User -> "User"
                | MessageRole.Assistant -> "AI"
                | MessageRole.System -> "System"
                | MessageRole.Tool -> "Tool"

            let cleaned = TextCleaning.cleanBlock message.Content
            (speaker, cleaned))
        |> List.filter (fun (_, content) -> not (String.IsNullOrWhiteSpace content))
        |> List.truncate 3
        |> List.map (fun (speaker, content) -> $"- {speaker}: {truncate 120 content}")
