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

    member _.ExecuteAsync(sessionId: string, commitMessage: string option) : Task<CommandResult> =
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
                    let noteMarkdown = Markdown.renderConversation committer session
                    let summary = Markdown.buildSummary session
                    output.Info "Session summary that will be attached to git notes:"
                    if List.isEmpty summary then
                        output.Info "- No non-empty messages found."
                    else
                        summary |> List.iter output.Info

                    Log.Debug("Creating git commit")
                    let! commitResult = git.CommitAsync(commitMessage)
                    match commitResult with
                    | Error commitError -> return CommandResult.Failed commitError
                    | Ok _ ->
                        let! hashResult = git.GetHeadHashAsync()
                        match hashResult with
                        | Error hashError -> return CommandResult.Failed hashError
                        | Ok hash ->
                            Log.Debug($"Adding note to commit {hash}")
                            let! noteResult = git.AddNoteAsync(hash, noteMarkdown)
                            match noteResult with
                            | Error noteError -> return CommandResult.Failed noteError
                            | Ok _ ->
                                output.Info($"Attached session '{sessionId}' as a git note to commit {hash}.")
                                return CommandResult.Completed
        }
