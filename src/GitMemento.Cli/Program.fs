open System
open GitMemento
open Serilog

let configureLogging () =
    let configuration =
        LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()

#if DEBUG
    configuration.MinimumLevel.Debug() |> ignore
#else
    configuration.MinimumLevel.Information() |> ignore
#endif
    Log.Logger <- configuration.CreateLogger()

let requireConfigured (git: IGitService) =
    match git.GetLocalConfigValueAsync("memento.provider").Result with
    | Error err -> Error err
    | Ok None ->
        Error "git-memento is not configured for this repository. Run: git memento init"
    | Ok(Some provider) -> Ok provider

let loadProviderSettings (git: IGitService) (providerValue: string) =
    match AiProviderFactory.normalizeProvider providerValue with
    | Error err -> Error err
    | Ok provider ->
        match AiProviderFactory.defaultSettings provider with
        | Error err -> Error err
        | Ok defaults ->
            let read key fallback =
                match git.GetLocalConfigValueAsync(key).Result with
                | Error err -> Error err
                | Ok(Some value) -> Ok value
                | Ok None -> Ok fallback

            let keyBase = $"memento.{provider}"
            match read $"{keyBase}.bin" defaults.Executable with
            | Error err -> Error err
            | Ok executable ->
                match read $"{keyBase}.getArgs" defaults.GetArgs with
                | Error err -> Error err
                | Ok getArgs ->
                    match read $"{keyBase}.listArgs" defaults.ListArgs with
                    | Error err -> Error err
                    | Ok listArgs ->
                        Ok
                            { Provider = provider
                              Executable = executable
                              GetArgs = getArgs
                              ListArgs = listArgs }

[<EntryPoint>]
let main args =
    configureLogging ()
    try
        match CliArgs.parse args with
        | Error usage ->
            Console.Error.WriteLine(usage)
            Console.Error.WriteLine()
            Console.Error.WriteLine(CliArgs.usage)
            1
        | Ok Command.Help ->
            Console.WriteLine(CliArgs.usage)
            0
        | Ok command ->
            let runner = ProcessCommandRunner() :> ICommandRunner
            let git = GitService(runner) :> IGitService
            let output = ConsoleOutput() :> IUserOutput
            let shareNotesWorkflow = ShareNotesWorkflow(git, output)
            let initWorkflow = InitWorkflow(git, output)

            match command with
            | Command.Init provider ->
                initWorkflow.ExecuteAsync(provider).Result
                |> function
                    | CommandResult.Completed -> 0
                    | CommandResult.Failed message ->
                        Console.Error.WriteLine(message)
                        1
            | Command.Commit(sessionId, messages) ->
                match requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok providerName ->
                    match loadProviderSettings git providerName with
                    | Error configError ->
                        Console.Error.WriteLine(configError)
                        1
                    | Ok settings ->
                        let provider = AiProviderFactory.createFromSettings runner settings
                        let workflow = CommitWorkflow(git, provider, output)
                        workflow.ExecuteAsync(sessionId, messages).Result
                        |> function
                            | CommandResult.Completed -> 0
                            | CommandResult.Failed message ->
                                Console.Error.WriteLine(message)
                                1
            | Command.ShareNotes(remote) ->
                match requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok _ ->
                    shareNotesWorkflow.ExecuteAsync(remote).Result
                    |> function
                        | CommandResult.Completed -> 0
                        | CommandResult.Failed message ->
                            Console.Error.WriteLine(message)
                            1
            | Command.Help ->
                Console.WriteLine(CliArgs.usage)
                0
    finally
        Log.CloseAndFlush()
