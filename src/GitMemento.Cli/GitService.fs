namespace GitMemento

open System
open System.Threading.Tasks

type IGitService =
    abstract member DefaultNotesRef: string
    abstract member EnsureInRepositoryAsync: unit -> Task<Result<unit, string>>
    abstract member GetLocalConfigValueAsync: key: string -> Task<Result<string option, string>>
    abstract member GetLocalConfigValuesAsync: key: string -> Task<Result<string list, string>>
    abstract member SetLocalConfigValueAsync: key: string * value: string -> Task<Result<unit, string>>
    abstract member GetCommitterAliasAsync: unit -> Task<string>
    abstract member CommitAsync: messages: string list -> Task<Result<unit, string>>
    abstract member CommitAmendAsync: messages: string list -> Task<Result<unit, string>>
    abstract member GetHeadHashAsync: unit -> Task<Result<string, string>>
    abstract member AddNoteAsync: hash: string * note: string -> Task<Result<unit, string>>
    abstract member AddNoteInRefAsync: notesRef: string * hash: string * note: string -> Task<Result<unit, string>>
    abstract member PushAsync: remote: string -> Task<Result<unit, string>>
    abstract member EnsureNotesFetchConfiguredAsync: remote: string -> Task<Result<unit, string>>
    abstract member ShareNotesAsync: remote: string -> Task<Result<unit, string>>
    abstract member GetRefObjectIdAsync: refName: string -> Task<Result<string option, string>>
    abstract member UpdateRefAsync: refName: string * objectId: string -> Task<Result<unit, string>>
    abstract member FetchNotesToNamespaceAsync: remote: string * namespaceRoot: string -> Task<Result<unit, string>>
    abstract member MergeNotesAsync: targetRef: string * sourceRef: string * strategy: string -> Task<Result<unit, string>>
    abstract member ListRemoteRefsAsync: remote: string * pattern: string -> Task<Result<string list, string>>
    abstract member ResolveCommitAsync: revision: string -> Task<Result<string, string>>
    abstract member GetCommitsInRangeAsync: rangeSpec: string -> Task<Result<string list, string>>
    abstract member GetNoteAsync: hash: string -> Task<Result<string option, string>>
    abstract member GetNoteInRefAsync: notesRef: string * hash: string -> Task<Result<string option, string>>
    abstract member AppendNoteAsync: hash: string * note: string -> Task<Result<unit, string>>
    abstract member AppendNoteInRefAsync: notesRef: string * hash: string * note: string -> Task<Result<unit, string>>

type GitService(runner: ICommandRunner) =
    [<Literal>]
    let DefaultNotesRef = "refs/notes/commits"

    let failWith stderr fallback =
        if String.IsNullOrWhiteSpace stderr then fallback else stderr

    interface IGitService with
        member _.DefaultNotesRef = DefaultNotesRef

        member _.EnsureInRepositoryAsync() =
            task {
                let! result = runner.RunCaptureAsync("git", [ "rev-parse"; "--is-inside-work-tree" ])
                if result.ExitCode = 0 && result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) then
                    return Ok()
                else
                    return Error(failWith result.StdErr "This directory is not inside a git repository.")
            }

        member _.GetLocalConfigValueAsync(key: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "config"; "--local"; "--get"; key ])
                if result.ExitCode = 0 then
                    let value = result.StdOut.Trim()
                    if String.IsNullOrWhiteSpace value then
                        return Ok None
                    else
                        return Ok(Some value)
                elif result.ExitCode = 1 then
                    return Ok None
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.SetLocalConfigValueAsync(key: string, value: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "config"; "--local"; key; value ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetLocalConfigValuesAsync(key: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "config"; "--get-all"; key ])
                if result.ExitCode = 0 then
                    let values =
                        result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun value -> value.Trim())
                        |> Array.filter (String.IsNullOrWhiteSpace >> not)
                        |> Array.toList
                    return Ok values
                elif result.ExitCode = 1 then
                    return Ok []
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetCommitterAliasAsync() =
            task {
                let! nameResult = runner.RunCaptureAsync("git", [ "config"; "--get"; "user.name" ])
                if nameResult.ExitCode = 0 && not (String.IsNullOrWhiteSpace nameResult.StdOut) then
                    return nameResult.StdOut.Trim()
                else
                    let! emailResult = runner.RunCaptureAsync("git", [ "config"; "--get"; "user.email" ])
                    if emailResult.ExitCode = 0 && not (String.IsNullOrWhiteSpace emailResult.StdOut) then
                        return emailResult.StdOut.Trim()
                    else
                        return "Developer"
            }

        member _.CommitAsync(messages: string list) =
            task {
                if not (List.isEmpty messages) then
                    let args =
                        [ "commit" ]
                        @ (messages |> List.collect (fun msg -> [ "-m"; msg ]))

                    let! result = runner.RunCaptureAsync("git", args)
                    if result.ExitCode = 0 then
                        return Ok()
                    else
                        return Error(failWith result.StdErr result.StdOut)
                else
                    let! exitCode = runner.RunStreamingAsync("git", [ "commit" ])
                    if exitCode = 0 then
                        return Ok()
                    else
                        return Error "git commit failed or was aborted."
            }

        member _.CommitAmendAsync(messages: string list) =
            task {
                if not (List.isEmpty messages) then
                    let args =
                        [ "commit"; "--amend" ]
                        @ (messages |> List.collect (fun msg -> [ "-m"; msg ]))

                    let! result = runner.RunCaptureAsync("git", args)
                    if result.ExitCode = 0 then
                        return Ok()
                    else
                        return Error(failWith result.StdErr result.StdOut)
                else
                    let! exitCode = runner.RunStreamingAsync("git", [ "commit"; "--amend" ])
                    if exitCode = 0 then
                        return Ok()
                    else
                        return Error "git commit --amend failed or was aborted."
            }

        member _.GetHeadHashAsync() =
            task {
                let! result = runner.RunCaptureAsync("git", [ "rev-parse"; "HEAD" ])
                if result.ExitCode = 0 && not (String.IsNullOrWhiteSpace result.StdOut) then
                    return Ok(result.StdOut.Trim())
                else
                    return Error(failWith result.StdErr "Unable to read HEAD commit hash.")
            }

        member _.AddNoteAsync(hash: string, note: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "add"; "-f"; "-m"; note; hash ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.AddNoteInRefAsync(notesRef: string, hash: string, note: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "--ref"; notesRef; "add"; "-f"; "-m"; note; hash ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.PushAsync(remote: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "push"; remote ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.EnsureNotesFetchConfiguredAsync(remote: string) =
            task {
                let key = $"remote.{remote}.fetch"
                let notesRef = "+refs/notes/*:refs/notes/*"
                let! getResult = runner.RunCaptureAsync("git", [ "config"; "--get-all"; key ])
                let alreadySet =
                    getResult.ExitCode = 0
                    && getResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       |> Array.exists (fun value -> value.Trim().Equals(notesRef, StringComparison.Ordinal))

                if alreadySet then
                    return Ok()
                else
                    let! addResult = runner.RunCaptureAsync("git", [ "config"; "--add"; key; notesRef ])
                    if addResult.ExitCode = 0 then
                        return Ok()
                    else
                        return Error(failWith addResult.StdErr addResult.StdOut)
            }

        member _.ShareNotesAsync(remote: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "push"; remote; "refs/notes/*" ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetRefObjectIdAsync(refName: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "rev-parse"; "--verify"; "--quiet"; refName ])
                if result.ExitCode = 0 then
                    let value = result.StdOut.Trim()
                    if String.IsNullOrWhiteSpace value then
                        return Ok None
                    else
                        return Ok(Some value)
                elif result.ExitCode = 1 then
                    return Ok None
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.UpdateRefAsync(refName: string, objectId: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "update-ref"; refName; objectId ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.FetchNotesToNamespaceAsync(remote: string, namespaceRoot: string) =
            task {
                let spec = $"refs/notes/*:{namespaceRoot}/*"
                let! result = runner.RunCaptureAsync("git", [ "fetch"; remote; spec ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.MergeNotesAsync(targetRef: string, sourceRef: string, strategy: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "--ref"; targetRef; "merge"; "-s"; strategy; sourceRef ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.ListRemoteRefsAsync(remote: string, pattern: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "ls-remote"; remote; pattern ])
                if result.ExitCode = 0 then
                    let refs =
                        result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun value -> value.Trim())
                        |> Array.filter (String.IsNullOrWhiteSpace >> not)
                        |> Array.toList
                    return Ok refs
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.ResolveCommitAsync(revision: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "rev-parse"; "--verify"; $"{revision}^{{commit}}" ])
                if result.ExitCode = 0 then
                    let value = result.StdOut.Trim()
                    if String.IsNullOrWhiteSpace value then
                        return Error $"Unable to resolve commit '{revision}'."
                    else
                        return Ok value
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetCommitsInRangeAsync(rangeSpec: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "rev-list"; "--reverse"; rangeSpec ])
                if result.ExitCode = 0 then
                    let commits =
                        result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun value -> value.Trim())
                        |> Array.filter (String.IsNullOrWhiteSpace >> not)
                        |> Array.toList
                    return Ok commits
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetNoteAsync(hash: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "show"; hash ])
                if result.ExitCode = 0 then
                    return Ok(Some result.StdOut)
                elif result.ExitCode = 1 then
                    return Ok None
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.GetNoteInRefAsync(notesRef: string, hash: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "--ref"; notesRef; "show"; hash ])
                if result.ExitCode = 0 then
                    return Ok(Some result.StdOut)
                elif result.ExitCode = 1 then
                    return Ok None
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.AppendNoteAsync(hash: string, note: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "append"; "-m"; note; hash ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }

        member _.AppendNoteInRefAsync(notesRef: string, hash: string, note: string) =
            task {
                let! result = runner.RunCaptureAsync("git", [ "notes"; "--ref"; notesRef; "append"; "-m"; note; hash ])
                if result.ExitCode = 0 then
                    return Ok()
                else
                    return Error(failWith result.StdErr result.StdOut)
            }
