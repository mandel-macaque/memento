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
let ``parse notes-sync with defaults`` () =
    let result = CliArgs.parse [| "notes-sync" |]
    match result with
    | Ok(Command.NotesSync(remote, strategy)) ->
        Assert.Equal("origin", remote)
        Assert.Equal("cat_sort_uniq", strategy)
    | _ -> failwith "Expected notes-sync command with defaults."

[<Fact>]
let ``parse notes-sync with remote and strategy`` () =
    let result = CliArgs.parse [| "notes-sync"; "upstream"; "--strategy"; "union" |]
    match result with
    | Ok(Command.NotesSync(remote, strategy)) ->
        Assert.Equal("upstream", remote)
        Assert.Equal("union", strategy)
    | _ -> failwith "Expected notes-sync command with explicit values."

[<Fact>]
let ``parse notes-rewrite-setup`` () =
    let result = CliArgs.parse [| "notes-rewrite-setup" |]
    match result with
    | Ok Command.NotesRewriteSetup -> ()
    | _ -> failwith "Expected notes-rewrite-setup command."

[<Fact>]
let ``parse notes-carry with explicit flags`` () =
    let result = CliArgs.parse [| "notes-carry"; "--onto"; "abc123"; "--from-range"; "main..feature" |]
    match result with
    | Ok(Command.NotesCarry(onto, fromRange)) ->
        Assert.Equal("abc123", onto)
        Assert.Equal("main..feature", fromRange)
    | _ -> failwith "Expected notes-carry command."

[<Fact>]
let ``parse init with explicit provider`` () =
    let result = CliArgs.parse [| "init"; "claude" |]
    match result with
    | Ok(Command.Init(Some provider)) -> Assert.Equal("claude", provider)
    | _ -> failwith "Expected init command with provider."

[<Fact>]
let ``parse commit helper returns expected command`` () =
    let result = CliArgs.Parsing.parseCommit [| "commit"; "s1"; "--message=subject" |]
    match result with
    | Ok(Command.Commit(id, messages)) ->
        Assert.Equal("s1", id)
        Assert.Equal<string list>([ "subject" ], messages)
    | _ -> failwith "Expected commit helper to return a parsed commit command."

[<Fact>]
let ``parse notes-sync helper supports mixed ordering`` () =
    let result = CliArgs.Parsing.parseNotesSync [| "notes-sync"; "--strategy=union"; "upstream" |]
    match result with
    | Ok(Command.NotesSync(remote, strategy)) ->
        Assert.Equal("upstream", remote)
        Assert.Equal("union", strategy)
    | _ -> failwith "Expected notes-sync helper to support strategy before remote."

[<Fact>]
let ``parse notes-carry helper rejects missing required flags`` () =
    let result = CliArgs.Parsing.parseNotesCarry [| "notes-carry"; "--onto"; "abc123" |]
    match result with
    | Error message ->
        Assert.Equal("Usage: git memento notes-carry --onto <commit> --from-range <base>..<head>", message)
    | _ -> failwith "Expected notes-carry helper to reject incomplete inputs."

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
        member _.GetRefObjectIdAsync(_refName: string) = Task.FromResult(Ok None)
        member _.UpdateRefAsync(_refName: string, _objectId: string) = Task.FromResult(Ok())
        member _.FetchNotesToNamespaceAsync(_remote: string, _namespaceRoot: string) = Task.FromResult(Ok())
        member _.MergeNotesAsync(_notesRef: string, _strategy: string) = Task.FromResult(Ok())
        member _.ResolveCommitAsync(_revision: string) = Task.FromResult(Ok("hash"))
        member _.GetCommitsInRangeAsync(_rangeSpec: string) = Task.FromResult(Ok [])
        member _.GetNoteAsync(_hash: string) = Task.FromResult(Ok None)
        member _.AppendNoteAsync(_hash: string, _note: string) = Task.FromResult(Ok())

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
        member _.GetRefObjectIdAsync(_refName: string) = Task.FromResult(Ok None)
        member _.UpdateRefAsync(_refName: string, _objectId: string) = Task.FromResult(Ok())
        member _.FetchNotesToNamespaceAsync(_remote: string, _namespaceRoot: string) = Task.FromResult(Ok())
        member _.MergeNotesAsync(_notesRef: string, _strategy: string) = Task.FromResult(Ok())
        member _.ResolveCommitAsync(_revision: string) = Task.FromResult(Ok("hash"))
        member _.GetCommitsInRangeAsync(_rangeSpec: string) = Task.FromResult(Ok [])
        member _.GetNoteAsync(_hash: string) = Task.FromResult(Ok None)
        member _.AppendNoteAsync(_hash: string, _note: string) = Task.FromResult(Ok())

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

type private NotesSyncSpyGitService() =
    let calls = ResizeArray<string * string>()
    let refs = Dictionary<string, string>(StringComparer.Ordinal)

    do
        refs["refs/notes/commits"] <- "local-oid"
        refs["refs/notes/remote/origin/commits"] <- "remote-oid"

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
        member _.PushAsync(_remote: string) = Task.FromResult(Ok())
        member _.EnsureNotesFetchConfiguredAsync(remote: string) =
            calls.Add("ensureNotes", remote)
            Task.FromResult(Ok())
        member _.ShareNotesAsync(remote: string) =
            calls.Add("shareNotes", remote)
            Task.FromResult(Ok())
        member _.GetRefObjectIdAsync(refName: string) =
            calls.Add("getRef", refName)
            let found, value = refs.TryGetValue(refName)
            Task.FromResult(Ok(if found then Some value else None))
        member _.UpdateRefAsync(refName: string, objectId: string) =
            calls.Add("updateRef", $"{refName}={objectId}")
            refs[refName] <- objectId
            Task.FromResult(Ok())
        member _.FetchNotesToNamespaceAsync(remote: string, namespaceRoot: string) =
            calls.Add("fetchNotes", $"{remote}:{namespaceRoot}")
            Task.FromResult(Ok())
        member _.MergeNotesAsync(notesRef: string, strategy: string) =
            calls.Add("mergeNotes", $"{notesRef}:{strategy}")
            Task.FromResult(Ok())
        member _.ResolveCommitAsync(_revision: string) = Task.FromResult(Ok("hash"))
        member _.GetCommitsInRangeAsync(_rangeSpec: string) = Task.FromResult(Ok [])
        member _.GetNoteAsync(_hash: string) = Task.FromResult(Ok None)
        member _.AppendNoteAsync(_hash: string, _note: string) = Task.FromResult(Ok())

[<Fact>]
let ``notes-sync workflow creates backup merges and shares`` () =
    let git = NotesSyncSpyGitService() :> IGitService
    let output = StubOutput()
    let workflow = NotesSyncWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("origin", "cat_sort_uniq").Result

    Assert.Equal(CommandResult.Completed, result)
    let spy = git :?> NotesSyncSpyGitService
    Assert.Contains(("ensureRepo", ""), spy.Calls)
    Assert.Contains(("ensureNotes", "origin"), spy.Calls)
    Assert.Contains(("fetchNotes", "origin:refs/notes/remote/origin"), spy.Calls)
    Assert.Contains(("mergeNotes", "refs/notes/remote/origin/commits:cat_sort_uniq"), spy.Calls)
    Assert.Contains(("shareNotes", "origin"), spy.Calls)
    Assert.Contains(output.InfoLines, fun line -> line.StartsWith("Created notes backup at 'refs/notes/memento-backups/", StringComparison.Ordinal))

type private RewriteSetupSpyGitService() =
    let calls = ResizeArray<string * string>()

    member _.Calls = calls |> Seq.toList

    interface IGitService with
        member _.EnsureInRepositoryAsync() =
            calls.Add("ensureRepo", "")
            Task.FromResult(Ok())
        member _.GetLocalConfigValueAsync(_key: string) = Task.FromResult(Ok None)
        member _.SetLocalConfigValueAsync(key: string, value: string) =
            calls.Add("setConfig", $"{key}={value}")
            Task.FromResult(Ok())
        member _.GetCommitterAliasAsync() = Task.FromResult("Dev")
        member _.CommitAsync(_messages) = Task.FromResult(Ok())
        member _.GetHeadHashAsync() = Task.FromResult(Ok("hash"))
        member _.AddNoteAsync(_, _) = Task.FromResult(Ok())
        member _.PushAsync(_remote: string) = Task.FromResult(Ok())
        member _.EnsureNotesFetchConfiguredAsync(_remote: string) = Task.FromResult(Ok())
        member _.ShareNotesAsync(_remote: string) = Task.FromResult(Ok())
        member _.GetRefObjectIdAsync(_refName: string) = Task.FromResult(Ok None)
        member _.UpdateRefAsync(_refName: string, _objectId: string) = Task.FromResult(Ok())
        member _.FetchNotesToNamespaceAsync(_remote: string, _namespaceRoot: string) = Task.FromResult(Ok())
        member _.MergeNotesAsync(_notesRef: string, _strategy: string) = Task.FromResult(Ok())
        member _.ResolveCommitAsync(_revision: string) = Task.FromResult(Ok("hash"))
        member _.GetCommitsInRangeAsync(_rangeSpec: string) = Task.FromResult(Ok [])
        member _.GetNoteAsync(_hash: string) = Task.FromResult(Ok None)
        member _.AppendNoteAsync(_hash: string, _note: string) = Task.FromResult(Ok())

[<Fact>]
let ``notes-rewrite-setup workflow stores rewrite config`` () =
    let git = RewriteSetupSpyGitService() :> IGitService
    let output = StubOutput()
    let workflow = NotesRewriteSetupWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync().Result

    Assert.Equal(CommandResult.Completed, result)
    let spy = git :?> RewriteSetupSpyGitService
    Assert.Equal<(string * string) list>(
        [ ("ensureRepo", "")
          ("setConfig", "notes.rewriteRef=refs/notes/commits")
          ("setConfig", "notes.rewriteMode=concatenate")
          ("setConfig", "notes.rewrite.rebase=true")
          ("setConfig", "notes.rewrite.amend=true") ],
        spy.Calls
    )

type private NotesCarrySpyGitService() =
    let calls = ResizeArray<string * string>()
    let notes = Dictionary<string, string>(StringComparer.Ordinal)

    do
        notes["c1"] <- "note one"
        notes["c2"] <- "note two"

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
        member _.PushAsync(_remote: string) = Task.FromResult(Ok())
        member _.EnsureNotesFetchConfiguredAsync(_remote: string) = Task.FromResult(Ok())
        member _.ShareNotesAsync(_remote: string) = Task.FromResult(Ok())
        member _.GetRefObjectIdAsync(_refName: string) = Task.FromResult(Ok None)
        member _.UpdateRefAsync(_refName: string, _objectId: string) = Task.FromResult(Ok())
        member _.FetchNotesToNamespaceAsync(_remote: string, _namespaceRoot: string) = Task.FromResult(Ok())
        member _.MergeNotesAsync(_notesRef: string, _strategy: string) = Task.FromResult(Ok())
        member _.ResolveCommitAsync(revision: string) =
            calls.Add("resolveCommit", revision)
            Task.FromResult(Ok "onto-commit")
        member _.GetCommitsInRangeAsync(rangeSpec: string) =
            calls.Add("range", rangeSpec)
            Task.FromResult(Ok [ "c1"; "c2"; "c3" ])
        member _.GetNoteAsync(hash: string) =
            calls.Add("getNote", hash)
            let found, value = notes.TryGetValue(hash)
            Task.FromResult(Ok(if found then Some value else None))
        member _.AppendNoteAsync(hash: string, note: string) =
            calls.Add("appendNote", hash)
            calls.Add("appendNoteBody", note)
            Task.FromResult(Ok())

[<Fact>]
let ``notes-carry workflow appends collected notes to target commit`` () =
    let git = NotesCarrySpyGitService() :> IGitService
    let output = StubOutput()
    let workflow = NotesCarryWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("squash-hash", "main..feature").Result

    Assert.Equal(CommandResult.Completed, result)
    let spy = git :?> NotesCarrySpyGitService
    Assert.Contains(("resolveCommit", "squash-hash"), spy.Calls)
    Assert.Contains(("range", "main..feature"), spy.Calls)
    Assert.Contains(("appendNote", "onto-commit"), spy.Calls)
    let body =
        spy.Calls
        |> List.pick (fun (name, value) -> if name = "appendNoteBody" then Some value else None)
    Assert.Contains("## Source Commit c1", body)
    Assert.Contains("note one", body)
    Assert.Contains("## Source Commit c2", body)
    Assert.Contains("note two", body)
