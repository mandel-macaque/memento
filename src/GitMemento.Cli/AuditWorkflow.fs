namespace GitMemento

open System
open System.Text.Json
open System.Threading.Tasks
open Serilog

type AuditWorkflow(git: IGitService, output: IUserOutput) =
    let renderText (summary: AuditSummary) (strict: bool) =
        output.Info($"Audit range: {summary.Range}")
        output.Info($"Commits scanned: {summary.CommitsScanned}")
        output.Info($"Missing notes: {summary.MissingNotes.Length}")
        output.Info($"Invalid notes: {summary.InvalidNotes.Length}")
        output.Info "Difference:"
        output.Info "- missing-note: no note exists for the commit in refs/notes/commits."
        output.Info "- invalid-note: a note exists, but required session markers/structure are not valid."

        for commit in summary.MissingNotes do
            output.Info($"missing-note {commit} // {AuditCore.issueToDescription MissingNote}")

        for item in summary.InvalidNotes do
            let codes = item.Issues |> List.map AuditCore.issueToCode |> String.concat ","
            let reasons = item.Issues |> List.map AuditCore.issueToDescription |> String.concat " | "
            output.Info($"invalid-note {item.Commit} {codes} // {reasons}")

        if strict then
            output.Info "Strict mode: invalid notes are treated as failures."

    let renderJson (summary: AuditSummary) (strict: bool) =
        let payload =
            {| range = summary.Range
               strict = strict
               commitsScanned = summary.CommitsScanned
               missingNotes = summary.MissingNotes
               invalidNotes =
                summary.InvalidNotes
                |> List.map (fun item ->
                    {| commit = item.Commit
                       issues = item.Issues |> List.map AuditCore.issueToCode |}) |}

        let json = JsonSerializer.Serialize(payload, JsonSerializerOptions(WriteIndented = true))
        output.Info json

    let shouldFail (summary: AuditSummary) (strict: bool) =
        if strict then
            not (List.isEmpty summary.MissingNotes) || not (List.isEmpty summary.InvalidNotes)
        else
            not (List.isEmpty summary.MissingNotes)

    member _.ExecuteAsync(rangeSpec: string option, strict: bool, outputFormat: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before audit")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                let resolvedRange = rangeSpec |> Option.defaultValue "HEAD"
                let! commitsResult = git.GetCommitsInRangeAsync(resolvedRange)
                match commitsResult with
                | Error err -> return CommandResult.Failed err
                | Ok commits ->
                    let mutable failure: string option = None
                    let collected = ResizeArray<AuditCommitResult>()

                    for commitHash in commits do
                        if failure.IsNone then
                            let! noteResult = git.GetNoteAsync(commitHash)
                            match noteResult with
                            | Error err -> failure <- Some err
                            | Ok None ->
                                collected.Add(
                                    { Commit = commitHash
                                      Issues = [ MissingNote ] }
                                )
                            | Ok(Some note) ->
                                let issues = AuditCore.validateNote note
                                collected.Add(
                                    { Commit = commitHash
                                      Issues = issues }
                                )

                    match failure with
                    | Some err -> return CommandResult.Failed err
                    | None ->
                        let summary = AuditCore.buildSummary resolvedRange (collected |> Seq.toList)
                        match outputFormat with
                        | "json" -> renderJson summary strict
                        | _ -> renderText summary strict

                        if shouldFail summary strict then
                            return CommandResult.Failed "Audit found note coverage problems."
                        else
                            return CommandResult.Completed
        }
