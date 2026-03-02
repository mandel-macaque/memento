namespace GitMemento

open System.Threading.Tasks
open Serilog

type NotesRewriteSetupWorkflow(git: IGitService, output: IUserOutput) =
    member _.ExecuteAsync() : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before configuring notes rewrite")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                let configToSet =
                    [ "notes.rewriteRef", "refs/notes/*"
                      "notes.rewriteMode", "concatenate"
                      "notes.rewrite.rebase", "true"
                      "notes.rewrite.amend", "true" ]

                let mutable failure: string option = None

                for key, value in configToSet do
                    if failure.IsNone then
                        Log.Debug("Setting local git config {Key}={Value}", key, value)
                        let! setResult = git.SetLocalConfigValueAsync(key, value)
                        match setResult with
                        | Ok _ -> ()
                        | Error err -> failure <- Some err

                match failure with
                | Some err -> return CommandResult.Failed err
                | None ->
                    output.Info("Configured git notes rewrite for rebase and amend.")
                    output.Info("Rewritten commits will carry notes from refs/notes/* using concatenate mode.")
                    return CommandResult.Completed
        }
