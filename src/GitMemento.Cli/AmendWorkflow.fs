namespace GitMemento

open System
open System.Threading.Tasks
open Serilog

type AmendWorkflow(git: IGitService, provider: IAiSessionProvider option, output: IUserOutput) =
    [<Literal>]
    let FullAuditNotesRef = "refs/notes/memento-full-audit"

    let resolveUserSkill (summarySkill: string option) =
        summarySkill
        |> Option.map (fun value -> value.Trim())
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.bind (fun value ->
            if value.Equals("default", StringComparison.OrdinalIgnoreCase) then
                None
            else
                Some value)

    let isApproved (value: string) =
        value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
        || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)

    let rec confirmSummaryAsync
        (session: SessionData)
        (userSkill: string option)
        (userPrompt: string option)
        (summaryLimits: SummaryGenerationLimits)
        : Task<Result<string, string>> =
        task {
            match provider with
            | None -> return Error "Unable to generate summary during amend: provider is not configured."
            | Some ai ->
                let! summaryResult =
                    ai.SummarizeSessionAsync(
                        { Session = session
                          UserSkill = userSkill
                          UserPrompt = userPrompt
                          MaxMessageChars = summaryLimits.MaxMessageChars
                          MaxTranscriptChars = summaryLimits.MaxTranscriptChars
                          MaxPromptChars = summaryLimits.MaxPromptChars
                          RequireFullSession = summaryLimits.RequireFullSession }
                    )

                match summaryResult with
                | Error err -> return Error $"Unable to generate summary: {err}"
                | Ok summary ->
                    output.Info "Generated session summary:"
                    output.Info summary
                    output.Info "Use this summary? [y/N]"
                    let confirmation = Console.ReadLine() |> Option.ofObj |> Option.defaultValue String.Empty
                    if isApproved confirmation then
                        return Ok summary
                    else
                        output.Info "Provide a prompt to regenerate the summary:"
                        let retryPrompt = Console.ReadLine() |> Option.ofObj |> Option.defaultValue String.Empty
                        if String.IsNullOrWhiteSpace retryPrompt then
                            return Error "Summary was rejected and no retry prompt was provided."
                        else
                            return! confirmSummaryAsync session userSkill (Some(retryPrompt.Trim())) summaryLimits
        }

    let tryFetchSessionEntries
        (sessionId: string)
        (committer: string)
        (summarySkill: string option)
        (summaryLimits: SummaryGenerationLimits)
        : Task<Result<string * string option, string>> =
        task {
            match provider with
            | None ->
                return Error "Unable to fetch a new session during amend: provider is not configured."
            | Some ai ->
                let! sessionResult = ai.GetSessionAsync(sessionId)
                match sessionResult with
                | Ok session ->
                    let fullConversationMarkdown = Markdown.renderConversation committer session
                    match summarySkill with
                    | None ->
                        let summary = Markdown.buildSummary session
                        output.Info "Session summary that will be attached to git notes:"
                        if List.isEmpty summary then
                            output.Info "- No non-empty messages found."
                        else
                            summary |> List.iter output.Info
                        return Ok(fullConversationMarkdown, None)
                    | Some _ ->
                        let userSkill = resolveUserSkill summarySkill
                        let! confirmedSummaryResult = confirmSummaryAsync session userSkill None summaryLimits
                        match confirmedSummaryResult with
                        | Error err -> return Error err
                        | Ok confirmedSummary ->
                            let originalLogHash = Markdown.originalSessionLogHash session
                            let summaryEntry = Markdown.renderSummaryEntry session confirmedSummary originalLogHash
                            let summaryHash = Markdown.sha256Hex summaryEntry
                            let fullAuditEntry =
                                Markdown.renderFullAuditEntry fullConversationMarkdown summaryHash originalLogHash
                            return Ok(summaryEntry, Some fullAuditEntry)
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

    member _.ExecuteAsync
        (sessionId: string option,
         commitMessages: string list,
         summarySkill: string option,
         summaryLimits: SummaryGenerationLimits)
        : Task<CommandResult> =
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
                        let! existingFullAuditNoteResult = git.GetNoteInRefAsync(FullAuditNotesRef, oldHead)
                        match existingFullAuditNoteResult with
                        | Error err -> return CommandResult.Failed err
                        | Ok existingFullAuditNote ->
                            let existingEntries =
                                existingNote
                                |> Option.map SessionNotes.parseEntries
                                |> Option.defaultValue []

                            let existingFullAuditEntries =
                                existingFullAuditNote
                                |> Option.map SessionNotes.parseEntries
                                |> Option.defaultValue []

                            let! committer = git.GetCommitterAliasAsync()
                            let! newEntryResult =
                                match sessionId with
                                | None ->
                                    if summarySkill.IsSome then
                                        Task.FromResult(Error "Flag --summary-skill requires an explicit session id with amend.")
                                    else
                                        Task.FromResult(Ok None)
                                | Some id ->
                                    task {
                                        let! entryResult = tryFetchSessionEntries id committer summarySkill summaryLimits
                                        return entryResult |> Result.map Some
                                    }

                            match newEntryResult with
                            | Error err -> return CommandResult.Failed err
                            | Ok newEntryWithAudit ->
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
                                            match newEntryWithAudit with
                                            | Some(value, _) -> existingEntries @ [ value ]
                                            | None -> existingEntries

                                        let combinedFullAuditEntries =
                                            match newEntryWithAudit with
                                            | Some(_, Some fullAuditEntry) -> existingFullAuditEntries @ [ fullAuditEntry ]
                                            | _ -> existingFullAuditEntries

                                        if List.isEmpty combinedEntries && List.isEmpty combinedFullAuditEntries then
                                            output.Info($"Amended commit {newHead} without attaching any memento sessions.")
                                            return CommandResult.Completed
                                        else
                                            let! addAuditResult =
                                                if List.isEmpty combinedFullAuditEntries then
                                                    Task.FromResult(Ok())
                                                else
                                                    let mergedFullAuditNote = SessionNotes.renderEntries combinedFullAuditEntries
                                                    git.AddNoteInRefAsync(FullAuditNotesRef, newHead, mergedFullAuditNote)

                                            if Result.isError addAuditResult then
                                                let noteError =
                                                    match addAuditResult with
                                                    | Error err -> err
                                                    | Ok _ -> String.Empty
                                                return CommandResult.Failed noteError
                                            else
                                                let! addResult =
                                                    if List.isEmpty combinedEntries then
                                                        Task.FromResult(Ok())
                                                    else
                                                        let mergedNote = SessionNotes.renderEntries combinedEntries
                                                        git.AddNoteAsync(newHead, mergedNote)

                                                if Result.isError addResult then
                                                    let noteError =
                                                        match addResult with
                                                        | Error err -> err
                                                        | Ok _ -> String.Empty
                                                    return CommandResult.Failed noteError
                                                else
                                                    output.Info(
                                                        $"Amended commit {newHead} and attached {combinedEntries.Length} session note(s)."
                                                    )
                                                    return CommandResult.Completed
        }
