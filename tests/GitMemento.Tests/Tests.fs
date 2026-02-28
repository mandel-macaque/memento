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
    | Ok(Command.Commit(id, Some message)) ->
        Assert.Equal("sess-123", id)
        Assert.Equal("ship feature", message)
    | _ -> failwith "Expected parsed commit command."

[<Fact>]
let ``parse commit without message`` () =
    let result = CliArgs.parse [| "commit"; "sess-123" |]
    match result with
    | Ok(Command.Commit(id, None)) -> Assert.Equal("sess-123", id)
    | _ -> failwith "Expected parsed commit command without message."

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
let ``parse init with explicit provider`` () =
    let result = CliArgs.parse [| "init"; "claude" |]
    match result with
    | Ok(Command.Init(Some provider)) -> Assert.Equal("claude", provider)
    | _ -> failwith "Expected init command with provider."

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
        member _.CommitAsync(_) = Task.FromResult(Ok())
        member _.GetHeadHashAsync() = Task.FromResult(Ok("hash"))
        member _.AddNoteAsync(_, _) = Task.FromResult(Ok())
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
