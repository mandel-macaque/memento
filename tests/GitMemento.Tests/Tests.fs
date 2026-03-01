module GitMemento.Tests

open System
open System.Collections.Generic
open System.Threading.Tasks
open GitMemento
open Xunit

[<Fact>]
let ``parse commit with explicit message`` () =
    let result = CliArgs.parse [| "commit"; "sess-123"; "-m"; "ship feature" |]
    match result with
    | Ok(Command.Commit(id, messages)) ->
        Assert.Equal("sess-123", id)
        Assert.Equal<string list>([ "ship feature" ], messages)
    | _ -> failwith "Expected parsed commit command."

[<Fact>]
let ``parse commit without message`` () =
    let result = CliArgs.parse [| "commit"; "sess-123" |]
    match result with
    | Ok(Command.Commit(id, messages)) ->
        Assert.Equal("sess-123", id)
        Assert.Empty(messages)
    | _ -> failwith "Expected parsed commit command without message."

[<Fact>]
let ``parse commit with multiple messages preserves order`` () =
    let result =
        CliArgs.parse
            [| "commit"
               "sess-123"
               "-m"
               "subject"
               "-m"
               "body paragraph"
               "-mfooter" |]

    match result with
    | Ok(Command.Commit(id, messages)) ->
        Assert.Equal("sess-123", id)
        Assert.Equal<string list>([ "subject"; "body paragraph"; "footer" ], messages)
    | _ -> failwith "Expected parsed commit command with multiple messages."

[<Fact>]
let ``parse commit supports --message forms`` () =
    let result =
        CliArgs.parse
            [| "commit"
               "sess-123"
               "--message"
               "subject"
               "--message=body paragraph" |]

    match result with
    | Ok(Command.Commit(id, messages)) ->
        Assert.Equal("sess-123", id)
        Assert.Equal<string list>([ "subject"; "body paragraph" ], messages)
    | _ -> failwith "Expected parsed commit command with --message variants."

[<Fact>]
let ``parse commit preserves raw message text`` () =
    let result = CliArgs.parse [| "commit"; "sess-123"; "-m"; "  subject with spacing  " |]

    match result with
    | Ok(Command.Commit(_, messages)) -> Assert.Equal<string list>([ "  subject with spacing  " ], messages)
    | _ -> failwith "Expected parsed commit command preserving message text."

[<Fact>]
let ``parse rejects unknown argument`` () =
    let result = CliArgs.parse [| "commit"; "sess-123"; "--bad" |]
    Assert.True(Result.isError result)

[<Fact>]
let ``parse share-notes with default remote`` () =
    let result = CliArgs.parse [| "share-notes" |]
    match result with
    | Ok(Command.ShareNotes remote) -> Assert.Equal("origin", remote)
    | _ -> failwith "Expected share-notes command with default remote."

[<Fact>]
let ``parse push with default remote`` () =
    let result = CliArgs.parse [| "push" |]
    match result with
    | Ok(Command.Push remote) -> Assert.Equal("origin", remote)
    | _ -> failwith "Expected push command with default remote."

[<Fact>]
let ``parse push with explicit remote`` () =
    let result = CliArgs.parse [| "push"; "upstream" |]
    match result with
    | Ok(Command.Push remote) -> Assert.Equal("upstream", remote)
    | _ -> failwith "Expected push command with explicit remote."

[<Fact>]
let ``parse init with explicit provider`` () =
    let result = CliArgs.parse [| "init"; "claude" |]
    match result with
    | Ok(Command.Init(Some provider)) -> Assert.Equal("claude", provider)
    | _ -> failwith "Expected init command with provider."

[<Fact>]
let ``parse version flags`` () =
    let forms = [| [| "version" |]; [| "--version" |]; [| "-v" |] |]
    forms
    |> Array.iter (fun args ->
        let input = String.Join(" ", args)
        match CliArgs.parse args with
        | Ok Command.Version -> ()
        | _ -> failwith $"Expected version command for input: {input}")

[<Fact>]
let ``cleaner strips log prefixes`` () =
    let cleaned = TextCleaning.cleanBlock "[INFO] hello\nDEBUG: world"
    Assert.Equal("hello\nworld", cleaned)

[<Fact>]
let ``markdown uses committer and provider aliases`` () =
    let session =
        { Id = "abc"
          Provider = "Codex"
          Title = None
          Messages =
            [ { Role = MessageRole.User
                Content = "Implement parser"
                Timestamp = None }
              { Role = MessageRole.Assistant
                Content = "Done."
                Timestamp = None } ] }

    let markdown = Markdown.renderConversation "Mandel" session

    Assert.Contains("### Mandel", markdown)
    Assert.Contains("### Codex", markdown)

[<Fact>]
let ``summary returns first relevant messages`` () =
    let session =
        { Id = "abc"
          Provider = "Codex"
          Title = None
          Messages =
            [ { Role = MessageRole.System
                Content = ""
                Timestamp = None }
              { Role = MessageRole.User
                Content = "Create unit tests"
                Timestamp = None }
              { Role = MessageRole.Assistant
                Content = "[DEBUG] Added tests"
                Timestamp = None } ] }

    let summary = Markdown.buildSummary session

    Assert.Equal(2, summary.Length)
    Assert.Contains("- User: Create unit tests", summary)
    Assert.Contains("- AI: Added tests", summary)

[<Fact>]
let ``provider factory supports claude`` () =
    let runner =
        { new ICommandRunner with
            member _.RunCaptureAsync(_, _) = Task.FromResult({ ExitCode = 1; StdOut = ""; StdErr = "" })
            member _.RunStreamingAsync(_, _) = Task.FromResult(1) }

    let settings =
        { Provider = "claude"
          Executable = "claude"
          GetArgs = "sessions get {id} --json"
          ListArgs = "sessions list --json" }

    let provider = AiProviderFactory.createFromSettings runner settings
    Assert.Equal("Claude", provider.Name)

type private StubOutput() =
    let info = ResizeArray<string>()
    let errors = ResizeArray<string>()

    member _.InfoLines = info |> Seq.toList
    member _.ErrorLines = errors |> Seq.toList

    interface IUserOutput with
        member _.Info(message: string) = info.Add(message)
        member _.Error(message: string) = errors.Add(message)

type private InMemoryGitService() =
    let config = Dictionary<string, string>(StringComparer.Ordinal)

    interface IGitService with
        member _.EnsureInRepositoryAsync() = Task.FromResult(Ok())
        member _.GetLocalConfigValueAsync(key: string) =
            let found, value = config.TryGetValue(key)
            Task.FromResult(Ok(if found then Some value else None))
        member _.SetLocalConfigValueAsync(key: string, value: string) =
            config[key] <- value
            Task.FromResult(Ok())
        member _.GetCommitterAliasAsync() = Task.FromResult("Dev")
        member _.CommitAsync(_messages) = Task.FromResult(Ok())
        member _.GetHeadHashAsync() = Task.FromResult(Ok("hash"))
        member _.AddNoteAsync(_, _) = Task.FromResult(Ok())
        member _.PushAsync(_) = Task.FromResult(Ok())
        member _.EnsureNotesFetchConfiguredAsync(_) = Task.FromResult(Ok())
        member _.ShareNotesAsync(_) = Task.FromResult(Ok())

[<Fact>]
let ``init workflow stores provider configuration in local git metadata`` () =
    let git = InMemoryGitService() :> IGitService
    let output = StubOutput()
    let workflow = InitWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync(Some "codex").Result

    Assert.Equal(CommandResult.Completed, result)
    let provider = git.GetLocalConfigValueAsync("memento.provider").Result
    match provider with
    | Ok(Some value) -> Assert.Equal("codex", value)
    | _ -> failwith "Expected memento.provider to be set."

type private PushSpyGitService() =
    let calls = ResizeArray<string * string>()

    member _.Calls = calls |> Seq.toList

    interface IGitService with
        member _.EnsureInRepositoryAsync() =
            calls.Add("ensureRepo", "")
            Task.FromResult(Ok())
        member _.GetLocalConfigValueAsync(_key: string) = Task.FromResult(Ok None)
        member _.SetLocalConfigValueAsync(_key: string, _value: string) = Task.FromResult(Ok())
        member _.GetCommitterAliasAsync() = Task.FromResult("Dev")
        member _.CommitAsync(_messages) = Task.FromResult(Ok())
        member _.GetHeadHashAsync() = Task.FromResult(Ok("hash"))
        member _.AddNoteAsync(_, _) = Task.FromResult(Ok())
        member _.PushAsync(remote: string) =
            calls.Add("push", remote)
            Task.FromResult(Ok())
        member _.EnsureNotesFetchConfiguredAsync(remote: string) =
            calls.Add("ensureNotes", remote)
            Task.FromResult(Ok())
        member _.ShareNotesAsync(remote: string) =
            calls.Add("shareNotes", remote)
            Task.FromResult(Ok())

[<Fact>]
let ``push workflow executes branch push then note sync`` () =
    let git = PushSpyGitService() :> IGitService
    let output = StubOutput()
    let workflow = PushWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("origin").Result

    Assert.Equal(CommandResult.Completed, result)
    let spy = git :?> PushSpyGitService
    Assert.Equal<(string * string) list>(
        [ ("ensureRepo", ""); ("push", "origin"); ("ensureNotes", "origin"); ("shareNotes", "origin") ],
        spy.Calls
    )
