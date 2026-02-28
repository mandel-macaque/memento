namespace GitMemento

open System.Threading.Tasks
open Serilog

type ShareNotesWorkflow(git: IGitService, output: IUserOutput) =
    member _.ExecuteAsync(remote: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before sharing notes")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                Log.Debug($"Ensuring notes fetch config for remote {remote}")
                let! configResult = git.EnsureNotesFetchConfiguredAsync(remote)
                match configResult with
                | Error configError -> return CommandResult.Failed configError
                | Ok _ ->
                    Log.Debug($"Pushing notes to remote {remote}")
                    let! shareResult = git.ShareNotesAsync(remote)
                    match shareResult with
                    | Error shareError -> return CommandResult.Failed shareError
                    | Ok _ ->
                        output.Info($"Shared git notes with remote '{remote}'.")
                        output.Info("Team members can run: git fetch <remote> refs/notes/*:refs/notes/*")
                        return CommandResult.Completed
        }
