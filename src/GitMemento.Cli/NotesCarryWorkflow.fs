namespace GitMemento

open System
open System.Text
open System.Threading.Tasks
open Serilog

type NotesCarryWorkflow(git: IGitService, output: IUserOutput) =
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

    member private _.CollectNotesAsync(commits: string list) : Task<Result<(string * string) list, string>> =
        task {
            let collected = ResizeArray<string * string>()
            let mutable failure: string option = None

            for commitHash in commits do
                if failure.IsNone then
                    let! noteResult = git.GetNoteAsync(commitHash)
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
                            let! collectedResult = this.CollectNotesAsync(commits)
                            match collectedResult with
                            | Error err -> return CommandResult.Failed err
                            | Ok [] ->
                                output.Info($"No notes found in source range '{fromRange}'.")
                                return CommandResult.Completed
                            | Ok notes ->
                                let markdown = NotesCarryWorkflow.BuildCarryMarkdown(fromRange, notes)
                                let! appendResult = git.AppendNoteAsync(ontoCommit, markdown)
                                match appendResult with
                                | Error err -> return CommandResult.Failed err
                                | Ok _ ->
                                    output.Info($"Carried {notes.Length} source note(s) from '{fromRange}' onto commit '{ontoCommit}'.")
                                    return CommandResult.Completed
        }
