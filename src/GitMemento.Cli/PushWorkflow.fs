namespace GitMemento

open System.Threading.Tasks
open Serilog

type PushWorkflow(git: IGitService, output: IUserOutput) =
    member _.ExecuteAsync(remote: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before push")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                Log.Debug("Pushing current branch to remote {Remote}", remote)
                let! pushResult = git.PushAsync(remote)
                match pushResult with
                | Error pushError -> return CommandResult.Failed pushError
                | Ok _ ->
                    Log.Debug("Ensuring notes fetch config for remote {Remote}", remote)
                    let! configResult = git.EnsureNotesFetchConfiguredAsync(remote)
                    match configResult with
                    | Error configError -> return CommandResult.Failed configError
                    | Ok _ ->
                        Log.Debug("Pushing notes to remote {Remote}", remote)
                        let! shareResult = git.ShareNotesAsync(remote)
                        match shareResult with
                        | Error shareError -> return CommandResult.Failed shareError
                        | Ok _ ->
                            output.Info($"Pushed branch and shared git notes with remote '{remote}'.")
                            output.Info("Team members can run: git fetch <remote> refs/notes/*:refs/notes/*")
                            return CommandResult.Completed
        }
