namespace GitMemento

open System
open System.Threading.Tasks

type IGitService =
    abstract member EnsureInRepositoryAsync: unit -> Task<Result<unit, string>>
    abstract member GetLocalConfigValueAsync: key: string -> Task<Result<string option, string>>
    abstract member SetLocalConfigValueAsync: key: string * value: string -> Task<Result<unit, string>>
    abstract member GetCommitterAliasAsync: unit -> Task<string>
    abstract member CommitAsync: messages: string list -> Task<Result<unit, string>>
    abstract member GetHeadHashAsync: unit -> Task<Result<string, string>>
    abstract member AddNoteAsync: hash: string * note: string -> Task<Result<unit, string>>
    abstract member EnsureNotesFetchConfiguredAsync: remote: string -> Task<Result<unit, string>>
    abstract member ShareNotesAsync: remote: string -> Task<Result<unit, string>>

type GitService(runner: ICommandRunner) =
    let failWith stderr fallback =
        if String.IsNullOrWhiteSpace stderr then fallback else stderr

    interface IGitService with
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
