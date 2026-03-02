namespace GitMemento

open System
open System.Security.Cryptography
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

    let sha256Hex (value: string) =
        let bytes = Encoding.UTF8.GetBytes(value)
        let hash = SHA256.HashData(bytes)
        Convert.ToHexString(hash).ToLowerInvariant()

    let originalSessionLogHash (session: SessionData) =
        let sb = StringBuilder(4096)
        sb.AppendLine($"Provider={session.Provider}") |> ignore
        sb.AppendLine($"SessionId={session.Id}") |> ignore
        sb.AppendLine($"SessionTitle={session.Title |> Option.defaultValue String.Empty}") |> ignore
        for message in session.Messages do
            let timestamp =
                message.Timestamp
                |> Option.map (fun ts -> ts.ToString("O"))
                |> Option.defaultValue String.Empty
            sb.AppendLine($"Role={message.Role}") |> ignore
            sb.AppendLine($"Timestamp={timestamp}") |> ignore
            sb.AppendLine("Content:") |> ignore
            sb.AppendLine(message.Content) |> ignore
            sb.AppendLine("---") |> ignore
        sha256Hex (sb.ToString())

    let renderSummaryEntry (session: SessionData) (summaryMarkdown: string) (originalSessionLogHash: string) =
        let sb = StringBuilder(1024)
        sb.AppendLine("# Git Memento Session Summary") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("- Session Kind: Summary") |> ignore
        sb.AppendLine($"- Provider: {session.Provider}") |> ignore
        sb.AppendLine($"- Session ID: {session.Id}") |> ignore
        sb.AppendLine($"- Original Session Log SHA256: {originalSessionLogHash}") |> ignore
        sb.AppendLine($"- Captured At (UTC): {DateTimeOffset.UtcNow:O}") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("## Summary") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(summaryMarkdown.Trim()) |> ignore
        sb.ToString().Trim() + Environment.NewLine

    let renderFullAuditEntry (conversationMarkdown: string) (summaryHash: string) (originalSessionLogHash: string) =
        let sb = StringBuilder(conversationMarkdown.Length + 256)
        sb.AppendLine("<!-- git-memento-full-audit:v1 -->") |> ignore
        sb.AppendLine($"- Session Kind: Full Session") |> ignore
        sb.AppendLine($"- Summary SHA256: {summaryHash}") |> ignore
        sb.AppendLine($"- Original Session Log SHA256: {originalSessionLogHash}") |> ignore
        sb.AppendLine() |> ignore
        sb.Append(conversationMarkdown.TrimEnd()) |> ignore
        sb.AppendLine() |> ignore
        sb.ToString()

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
