namespace GitMemento

open System
open System.Text
open System.Threading.Tasks
open Serilog

type NotesCarryWorkflow(git: IGitService, output: IUserOutput) =
    [<Literal>]
    let FullAuditNotesRef = "refs/notes/memento-full-audit"

    static member private InvalidRangeMessage =
        "Invalid --from-range value. Expected format: <base>..<head>"

    static member private IsValidRangeSpec(fromRange: string) =
        fromRange.Contains("..", StringComparison.Ordinal)

    static member private BuildCarryMarkdown(fromRange: string, notes: (string * string) list) =
        let markdown = StringBuilder()

        // Keep explicit provenance so audit trails remain readable after squash/rewrite.
        markdown.AppendLine("# Git Memento Carried Notes") |> ignore
        markdown.AppendLine($"- Source range: {fromRange}") |> ignore
        markdown.AppendLine($"- Carried at: {DateTimeOffset.UtcNow:O}") |> ignore
        markdown.AppendLine() |> ignore

        for commitHash, note in notes do
            markdown.AppendLine($"## Source Commit {commitHash}") |> ignore
            markdown.AppendLine(note.TrimEnd()) |> ignore
            markdown.AppendLine() |> ignore

        markdown.ToString().TrimEnd()

    member private _.CollectNotesAsync
        (commits: string list, notesRef: string option)
        : Task<Result<(string * string) list, string>> =
        task {
            let collected = ResizeArray<string * string>()
            let mutable failure: string option = None

            for commitHash in commits do
                if failure.IsNone then
                    let! noteResult =
                        match notesRef with
                        | None -> git.GetNoteAsync(commitHash)
                        | Some refName -> git.GetNoteInRefAsync(refName, commitHash)
                    match noteResult with
                    | Error err -> failure <- Some err
                    | Ok None -> ()
                    | Ok(Some note) -> collected.Add(commitHash, note)

            match failure with
            | Some err -> return Error err
            | None -> return Ok(collected |> Seq.toList)
        }

    member this.ExecuteAsync(onto: string, fromRange: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before carrying notes")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                if not (NotesCarryWorkflow.IsValidRangeSpec(fromRange)) then
                    return CommandResult.Failed NotesCarryWorkflow.InvalidRangeMessage
                else
                    let! ontoResult = git.ResolveCommitAsync(onto)
                    match ontoResult with
                    | Error err -> return CommandResult.Failed err
                    | Ok ontoCommit ->
                        let! commitsResult = git.GetCommitsInRangeAsync(fromRange)
                        match commitsResult with
                        | Error err -> return CommandResult.Failed err
                        | Ok commits when List.isEmpty commits ->
                            output.Info($"No commits found in range '{fromRange}'.")
                            return CommandResult.Completed
                        | Ok commits ->
                            let! defaultNotesResult = this.CollectNotesAsync(commits, None)
                            match defaultNotesResult with
                            | Error err -> return CommandResult.Failed err
                            | Ok defaultNotes ->
                                let! auditNotesResult = this.CollectNotesAsync(commits, Some FullAuditNotesRef)
                                match auditNotesResult with
                                | Error err -> return CommandResult.Failed err
                                | Ok auditNotes ->
                                    if List.isEmpty defaultNotes && List.isEmpty auditNotes then
                                        output.Info($"No notes found in source range '{fromRange}'.")
                                        return CommandResult.Completed
                                    else
                                        let! appendAuditResult =
                                            if List.isEmpty auditNotes then
                                                Task.FromResult(Ok())
                                            else
                                                let auditMarkdown = NotesCarryWorkflow.BuildCarryMarkdown(fromRange, auditNotes)
                                                git.AppendNoteInRefAsync(FullAuditNotesRef, ontoCommit, auditMarkdown)

                                        if Result.isError appendAuditResult then
                                            let err =
                                                match appendAuditResult with
                                                | Error value -> value
                                                | Ok _ -> String.Empty
                                            return CommandResult.Failed err
                                        else
                                            let! appendResult =
                                                if List.isEmpty defaultNotes then
                                                    Task.FromResult(Ok())
                                                else
                                                    let markdown = NotesCarryWorkflow.BuildCarryMarkdown(fromRange, defaultNotes)
                                                    git.AppendNoteAsync(ontoCommit, markdown)

                                            if Result.isError appendResult then
                                                let err =
                                                    match appendResult with
                                                    | Error value -> value
                                                    | Ok _ -> String.Empty
                                                return CommandResult.Failed err
                                            else
                                                output.Info(
                                                    $"Carried {defaultNotes.Length} default note(s) and {auditNotes.Length} full-audit note(s) from '{fromRange}' onto commit '{ontoCommit}'."
                                                )
                                                return CommandResult.Completed
        }
