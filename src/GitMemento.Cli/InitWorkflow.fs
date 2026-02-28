namespace GitMemento

open System
open System.Threading.Tasks
open Serilog

type InitWorkflow(git: IGitService, output: IUserOutput) =
    let promptProvider () =
        output.Info "Select AI provider:"
        output.Info "1. Codex"
        output.Info "2. Claude"
        output.Info "Enter choice [1/2]:"
        let choice = Console.ReadLine()
        match choice with
        | "1" -> Some "codex"
        | "2" -> Some "claude"
        | _ -> None

    let keyForProvider provider suffix = $"memento.{provider}.{suffix}"

    member _.ExecuteAsync(providerArg: string option) : Task<CommandResult> =
        task {
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                let providerInput =
                    match providerArg with
                    | Some explicitProvider -> Some explicitProvider
                    | None -> promptProvider ()

                match providerInput with
                | None -> return CommandResult.Failed "Invalid provider selection. Run: git memento init [codex|claude]"
                | Some rawProvider ->
                    match AiProviderFactory.defaultSettings rawProvider with
                    | Error err -> return CommandResult.Failed err
                    | Ok settings ->
                        Log.Debug($"Initializing memento configuration with provider {settings.Provider}")
                        let writes =
                            [ ("memento.provider", settings.Provider)
                              (keyForProvider settings.Provider "bin", settings.Executable)
                              (keyForProvider settings.Provider "getArgs", settings.GetArgs)
                              (keyForProvider settings.Provider "listArgs", settings.ListArgs) ]

                        let mutable writeError: string option = None
                        for (key, value) in writes do
                            if writeError.IsNone then
                                let! result = git.SetLocalConfigValueAsync(key, value)
                                match result with
                                | Ok _ -> ()
                                | Error err -> writeError <- Some err

                        match writeError with
                        | Some err -> return CommandResult.Failed err
                        | None ->
                            output.Info($"Initialized git-memento for provider '{settings.Provider}'.")
                            output.Info "Configuration saved in local git metadata (git config --local memento.*)."
                            return CommandResult.Completed
        }
