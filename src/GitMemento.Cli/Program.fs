open System
open System.Reflection
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

let formatVersion (runner: ICommandRunner) =
    let assemblyVersion =
        Assembly.GetEntryAssembly()
        |> Option.ofObj
        |> Option.bind (fun assembly -> assembly.GetName().Version |> Option.ofObj)
        |> Option.defaultValue (new Version(0, 0))

    let majorMinor = $"{assemblyVersion.Major}.{assemblyVersion.Minor}"

    let tryCapture (args: string list) =
        try
            let result = runner.RunCaptureAsync("git", args).Result
            if result.ExitCode = 0 then
                let value = result.StdOut.Trim()
                if String.IsNullOrWhiteSpace(value) then None else Some value
            else
                None
        with _ ->
            None

    let commitHash = tryCapture [ "rev-parse"; "--short"; "HEAD" ]
    let commitDate = tryCapture [ "show"; "-s"; "--format=%cI"; "HEAD" ]

    match commitHash, commitDate with
    | Some hash, Some date -> $"git-memento {majorMinor} (commit {hash}, date {date})"
    | Some hash, None -> $"git-memento {majorMinor} (commit {hash})"
    | None, Some date -> $"git-memento {majorMinor} (commit date {date})"
    | None, None -> $"git-memento {majorMinor} (commit metadata unavailable)"

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
            let pushWorkflow = PushWorkflow(git, output)
            let notesSyncWorkflow = NotesSyncWorkflow(git, output)
            let notesRewriteSetupWorkflow = NotesRewriteSetupWorkflow(git, output)
            let notesCarryWorkflow = NotesCarryWorkflow(git, output)
            let auditWorkflow = AuditWorkflow(git, output)
            let doctorWorkflow = DoctorWorkflow(git, runner, output)
            let initWorkflow = InitWorkflow(git, output)

            match command with
            | Command.Version ->
                Console.WriteLine(formatVersion runner)
                0
            | Command.Init provider ->
                initWorkflow.ExecuteAsync(provider).Result
                |> function
                    | CommandResult.Completed -> 0
                    | CommandResult.Failed message ->
                        Console.Error.WriteLine(message)
                        1
            | Command.Audit(rangeSpec, strict, outputFormat) ->
                auditWorkflow.ExecuteAsync(rangeSpec, strict, outputFormat).Result
                |> function
                    | CommandResult.Completed -> 0
                    | CommandResult.Failed message ->
                        Console.Error.WriteLine(message)
                        1
            | Command.Doctor(remote, outputFormat) ->
                doctorWorkflow.ExecuteAsync(remote, outputFormat).Result
                |> function
                    | CommandResult.Completed -> 0
                    | CommandResult.Failed message ->
                        Console.Error.WriteLine(message)
                        1
            | Command.Commit(sessionId, messages, summarySkill) ->
                match MementoConfig.requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok providerName ->
                    match MementoConfig.loadProviderSettings git providerName with
                    | Error configError ->
                        Console.Error.WriteLine(configError)
                        1
                    | Ok settings ->
                        let provider = AiProviderFactory.createFromSettings runner settings
                        let workflow = CommitWorkflow(git, provider, output)
                        workflow.ExecuteAsync(sessionId, messages, summarySkill).Result
                        |> function
                            | CommandResult.Completed -> 0
                            | CommandResult.Failed message ->
                                Console.Error.WriteLine(message)
                                1
            | Command.Amend(sessionId, messages, summarySkill) ->
                let workflowWithProvider (provider: IAiSessionProvider option) =
                    let workflow = AmendWorkflow(git, provider, output)
                    workflow.ExecuteAsync(sessionId, messages, summarySkill).Result
                    |> function
                        | CommandResult.Completed -> 0
                        | CommandResult.Failed message ->
                            Console.Error.WriteLine(message)
                            1

                match sessionId with
                | None -> workflowWithProvider None
                | Some _ ->
                    match MementoConfig.requireConfigured git with
                    | Error configError ->
                        Console.Error.WriteLine(configError)
                        1
                    | Ok providerName ->
                        match MementoConfig.loadProviderSettings git providerName with
                        | Error configError ->
                            Console.Error.WriteLine(configError)
                            1
                        | Ok settings ->
                            let provider = AiProviderFactory.createFromSettings runner settings
                            workflowWithProvider (Some provider)
            | Command.ShareNotes(remote) ->
                match MementoConfig.requireConfigured git with
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
            | Command.Push(remote) ->
                match MementoConfig.requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok _ ->
                    pushWorkflow.ExecuteAsync(remote).Result
                    |> function
                        | CommandResult.Completed -> 0
                        | CommandResult.Failed message ->
                            Console.Error.WriteLine(message)
                            1
            | Command.NotesSync(remote, strategy) ->
                match MementoConfig.requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok _ ->
                    notesSyncWorkflow.ExecuteAsync(remote, strategy).Result
                    |> function
                        | CommandResult.Completed -> 0
                        | CommandResult.Failed message ->
                            Console.Error.WriteLine(message)
                            1
            | Command.NotesRewriteSetup ->
                match MementoConfig.requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok _ ->
                    notesRewriteSetupWorkflow.ExecuteAsync().Result
                    |> function
                        | CommandResult.Completed -> 0
                        | CommandResult.Failed message ->
                            Console.Error.WriteLine(message)
                            1
            | Command.NotesCarry(onto, fromRange) ->
                match MementoConfig.requireConfigured git with
                | Error configError ->
                    Console.Error.WriteLine(configError)
                    1
                | Ok _ ->
                    notesCarryWorkflow.ExecuteAsync(onto, fromRange).Result
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
