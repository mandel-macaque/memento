namespace GitMemento

open System
open System.Text.RegularExpressions

type AuditIssue =
    | MissingNote
    | MissingProviderMarker
    | MissingSessionIdMarker
    | EmptyNote

type AuditCommitResult =
    { Commit: string
      Issues: AuditIssue list }

type AuditSummary =
    { Range: string
      CommitsScanned: int
      MissingNotes: string list
      InvalidNotes: AuditCommitResult list }

module AuditCore =
    let private providerPattern =
        Regex(@"(?m)^\s*-\s*Provider:\s*.+$", RegexOptions.Compiled)

    let private sessionPattern =
        Regex(@"(?m)^\s*-\s*Session ID:\s*.+$", RegexOptions.Compiled)

    let validateNote (note: string) : AuditIssue list =
        let normalized = note.Replace("\r\n", "\n").Trim()
        if String.IsNullOrWhiteSpace normalized then
            [ EmptyNote ]
        else
            let entries = SessionNotes.parseEntries normalized
            if List.isEmpty entries then
                [ EmptyNote ]
            else
                let mutable hasProvider = true
                let mutable hasSession = true
                for entry in entries do
                    if not (providerPattern.IsMatch(entry)) then
                        hasProvider <- false
                    if not (sessionPattern.IsMatch(entry)) then
                        hasSession <- false

                let issues = ResizeArray<AuditIssue>()
                if not hasProvider then
                    issues.Add(MissingProviderMarker)
                if not hasSession then
                    issues.Add(MissingSessionIdMarker)
                issues |> Seq.toList

    let issueToCode = function
        | MissingNote -> "missing-note"
        | MissingProviderMarker -> "missing-provider-marker"
        | MissingSessionIdMarker -> "missing-session-id-marker"
        | EmptyNote -> "empty-note"

    let issueToDescription = function
        | MissingNote -> "No git note was found in refs/notes/commits for this commit."
        | MissingProviderMarker -> "A note exists, but it is missing the '- Provider:' marker."
        | MissingSessionIdMarker -> "A note exists, but it is missing the '- Session ID:' marker."
        | EmptyNote -> "A note exists, but its body is empty or not parseable as a session entry."

    let buildSummary (rangeSpec: string) (results: AuditCommitResult list) =
        let missing =
            results
            |> List.filter (fun item -> item.Issues |> List.contains MissingNote)
            |> List.map (fun item -> item.Commit)

        let invalid =
            results
            |> List.filter (fun item -> item.Issues |> List.exists (fun issue -> issue <> MissingNote))

        { Range = rangeSpec
          CommitsScanned = results.Length
          MissingNotes = missing
          InvalidNotes = invalid }
