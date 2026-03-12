namespace GitMemento

open System
open System.Threading.Tasks
open Serilog

type IUserOutput =
    abstract member Info: string -> unit
    abstract member Error: string -> unit

type ConsoleOutput() =
    interface IUserOutput with
        member _.Info(message: string) = Console.WriteLine(message)
        member _.Error(message: string) = Console.Error.WriteLine(message)

type CommitWorkflow(git: IGitService, provider: IAiSessionProvider, output: IUserOutput) =
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
            let! summaryResult =
                provider.SummarizeSessionAsync(
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

    let printSessionNotFound (sessionId: string) (sessions: SessionRef list) =
        output.Error($"Session '{sessionId}' was not found in {provider.Name}.")
        if List.isEmpty sessions then
            output.Error "No available sessions were returned by the provider."
        else
            output.Error "Available sessions:"
            sessions
            |> List.truncate 10
            |> List.iter (fun item ->
                match item.Title with
                | Some title -> output.Error($"- {item.Id} ({title})")
                | None -> output.Error($"- {item.Id}"))

    member _.ExecuteAsync
        (sessionId: string, commitMessages: string list, summarySkill: string option, summaryLimits: SummaryGenerationLimits)
        : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                Log.Debug("Reading committer alias")
                let! committer = git.GetCommitterAliasAsync()

                Log.Debug("Fetching session {SessionId} from provider {Provider}", sessionId, provider.Name)
                let! sessionResult = provider.GetSessionAsync(sessionId)
                match sessionResult with
                | Error fetchError ->
                    Log.Debug("Session fetch failed: {Error}", fetchError)
                    let! sessionsResult = provider.ListSessionsAsync()
                    match sessionsResult with
                    | Ok sessions -> printSessionNotFound sessionId sessions
                    | Error listError ->
                        output.Error($"Session '{sessionId}' could not be fetched.")
                        output.Error($"Provider error: {fetchError}")
                        output.Error($"Unable to list available sessions: {listError}")
                    return CommandResult.Failed $"Unable to fetch session '{sessionId}'."
                | Ok session ->
                    Log.Debug("Rendering markdown note for session {SessionId}", sessionId)
                    let fullConversationMarkdown = Markdown.renderConversation committer session
                    let! noteSelectionResult =
                        match summarySkill with
                        | None ->
                            task {
                                let quickSummary = Markdown.buildSummary session
                                output.Info "Session summary that will be attached to git notes:"
                                if List.isEmpty quickSummary then
                                    output.Info "- No non-empty messages found."
                                else
                                    quickSummary |> List.iter output.Info

                                return Ok(SessionNotes.renderEntries [ fullConversationMarkdown ], None)
                            }
                        | Some _ ->
                            task {
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
                                    return
                                        Ok(
                                            SessionNotes.renderEntries [ summaryEntry ],
                                            Some(SessionNotes.renderEntries [ fullAuditEntry ])
                                        )
                            }

                    match noteSelectionResult with
                    | Error err -> return CommandResult.Failed err
                    | Ok(noteMarkdown, fullAuditNoteMarkdown) ->
                        Log.Debug("Creating git commit")
                        let! commitResult = git.CommitAsync(commitMessages)
                        match commitResult with
                        | Error commitError -> return CommandResult.Failed commitError
                        | Ok _ ->
                            let! hashResult = git.GetHeadHashAsync()
                            match hashResult with
                            | Error hashError -> return CommandResult.Failed hashError
                            | Ok hash ->
                                Log.Debug($"Adding note to commit {hash}")
                                match fullAuditNoteMarkdown with
                                | None ->
                                    let! noteResult = git.AddNoteAsync(hash, noteMarkdown)
                                    match noteResult with
                                    | Error noteError -> return CommandResult.Failed noteError
                                    | Ok _ ->
                                        output.Info($"Attached session '{sessionId}' as a git note to commit {hash}.")
                                        return CommandResult.Completed
                                | Some fullAuditNote ->
                                    let! fullAuditResult = git.AddNoteInRefAsync(FullAuditNotesRef, hash, fullAuditNote)
                                    match fullAuditResult with
                                    | Error err -> return CommandResult.Failed err
                                    | Ok _ ->
                                        let! noteResult = git.AddNoteAsync(hash, noteMarkdown)
                                        match noteResult with
                                        | Error noteError -> return CommandResult.Failed noteError
                                        | Ok _ ->
                                            output.Info(
                                                $"Attached summary for session '{sessionId}' and stored full session in '{FullAuditNotesRef}' on commit {hash}."
                                            )
                                            return CommandResult.Completed
        }
