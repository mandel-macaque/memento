namespace GitMemento

open System.Threading.Tasks
open Serilog

type AmendWorkflow(git: IGitService, provider: IAiSessionProvider option, output: IUserOutput) =
    let tryFetchSessionEntry (sessionId: string) (committer: string) : Task<Result<string, string>> =
        task {
            match provider with
            | None ->
                return Error "Unable to fetch a new session during amend: provider is not configured."
            | Some ai ->
                let! sessionResult = ai.GetSessionAsync(sessionId)
                match sessionResult with
                | Ok session ->
                    let summary = Markdown.buildSummary session
                    output.Info "Session summary that will be attached to git notes:"
                    if List.isEmpty summary then
                        output.Info "- No non-empty messages found."
                    else
                        summary |> List.iter output.Info
                    return Ok(Markdown.renderConversation committer session)
                | Error fetchError ->
                    Log.Debug("Session fetch failed during amend: {Error}", fetchError)
                    let! sessionsResult = ai.ListSessionsAsync()
                    match sessionsResult with
                    | Ok sessions when List.isEmpty sessions ->
                        output.Error($"Session '{sessionId}' was not found in {ai.Name}.")
                        output.Error "No available sessions were returned by the provider."
                    | Ok sessions ->
                        output.Error($"Session '{sessionId}' was not found in {ai.Name}.")
                        output.Error "Available sessions:"
                        sessions
                        |> List.truncate 10
                        |> List.iter (fun item ->
                            match item.Title with
                            | Some title -> output.Error($"- {item.Id} ({title})")
                            | None -> output.Error($"- {item.Id}"))
                    | Error listError ->
                        output.Error($"Session '{sessionId}' could not be fetched.")
                        output.Error($"Provider error: {fetchError}")
                        output.Error($"Unable to list available sessions: {listError}")

                    return Error $"Unable to fetch session '{sessionId}'."
        }

    member _.ExecuteAsync(sessionId: string option, commitMessages: string list) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                Log.Debug("Resolving current HEAD before amend")
                let! oldHeadResult = git.GetHeadHashAsync()
                match oldHeadResult with
                | Error err -> return CommandResult.Failed err
                | Ok oldHead ->
                    Log.Debug("Reading existing note for commit {Hash}", oldHead)
                    let! existingNoteResult = git.GetNoteAsync(oldHead)
                    match existingNoteResult with
                    | Error err -> return CommandResult.Failed err
                    | Ok existingNote ->
                        let existingEntries =
                            existingNote
                            |> Option.map SessionNotes.parseEntries
                            |> Option.defaultValue []

                        let! committer = git.GetCommitterAliasAsync()
                        let! newEntryResult =
                            match sessionId with
                            | None -> Task.FromResult(Ok None)
                            | Some id ->
                                task {
                                    let! entryResult = tryFetchSessionEntry id committer
                                    return entryResult |> Result.map Some
                                }

                        match newEntryResult with
                        | Error err -> return CommandResult.Failed err
                        | Ok newEntry ->
                            Log.Debug("Creating amended commit")
                            let! amendResult = git.CommitAmendAsync(commitMessages)
                            match amendResult with
                            | Error amendError -> return CommandResult.Failed amendError
                            | Ok _ ->
                                let! newHeadResult = git.GetHeadHashAsync()
                                match newHeadResult with
                                | Error hashError -> return CommandResult.Failed hashError
                                | Ok newHead ->
                                    let combinedEntries =
                                        match newEntry with
                                        | Some value -> existingEntries @ [ value ]
                                        | None -> existingEntries

                                    if List.isEmpty combinedEntries then
                                        output.Info($"Amended commit {newHead} without attaching any memento sessions.")
                                        return CommandResult.Completed
                                    else
                                        let mergedNote = SessionNotes.renderEntries combinedEntries
                                        let! addResult = git.AddNoteAsync(newHead, mergedNote)
                                        match addResult with
                                        | Error noteError -> return CommandResult.Failed noteError
                                        | Ok _ ->
                                            output.Info($"Amended commit {newHead} and attached {combinedEntries.Length} session note(s).")
                                            return CommandResult.Completed
        }
