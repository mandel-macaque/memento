namespace GitMemento

open System
open System.Text.Json
open System.Threading.Tasks

type private DoctorCheckStatus =
    | Pass
    | Warn
    | Fail

type private DoctorCheck =
    { Name: string
      Status: DoctorCheckStatus
      Detail: string }

type DoctorWorkflow(git: IGitService, runner: ICommandRunner, output: IUserOutput) =
    let add (checks: ResizeArray<DoctorCheck>) name status detail =
        checks.Add(
            { Name = name
              Status = status
              Detail = detail }
        )

    let statusCode = function
        | Pass -> "PASS"
        | Warn -> "WARN"
        | Fail -> "FAIL"

    let renderText remote checks =
        output.Info($"Doctor remote: {remote}")
        for item in checks do
            output.Info($"[{statusCode item.Status}] {item.Name}: {item.Detail}")

    let renderJson remote checks =
        let payload =
            {| remote = remote
               checks =
                checks
                |> Seq.map (fun item ->
                    {| name = item.Name
                       status = statusCode item.Status
                       detail = item.Detail |})
                |> Seq.toList |}

        output.Info(JsonSerializer.Serialize(payload, JsonSerializerOptions(WriteIndented = true)))

    let hasFailure checks =
        checks
        |> Seq.exists (fun item -> item.Status = Fail)

    member _.ExecuteAsync(remote: string, outputFormat: string) : Task<CommandResult> =
        task {
            let checks = ResizeArray<DoctorCheck>()

            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message ->
                add checks "repository" Fail message
            | Ok _ ->
                add checks "repository" Pass "Inside a git work tree."

            let providerResult = git.GetLocalConfigValueAsync("memento.provider").Result
            let mutable configuredProvider: string option = None
            match providerResult with
            | Error err ->
                add checks "provider-config" Fail err
            | Ok None ->
                add checks "provider-config" Fail "memento.provider is not configured."
            | Ok(Some providerValue) ->
                match AiProviderFactory.normalizeProvider providerValue with
                | Error err ->
                    add checks "provider-config" Fail err
                | Ok normalized ->
                    configuredProvider <- Some normalized
                    add checks "provider-config" Pass $"Configured provider: {normalized}"

            match configuredProvider with
            | None -> ()
            | Some provider ->
                match MementoConfig.loadProviderSettings git provider with
                | Error err ->
                    add checks "provider-settings" Fail err
                | Ok settings ->
                    let keyBase = $"memento.{provider}"
                    let keyChecks =
                        [ $"{keyBase}.bin", settings.Executable
                          $"{keyBase}.getArgs", settings.GetArgs
                          $"{keyBase}.listArgs", settings.ListArgs
                          $"{keyBase}.summary.bin", settings.SummaryExecutable
                          $"{keyBase}.summary.args", settings.SummaryArgs ]

                    for key, fallback in keyChecks do
                        match git.GetLocalConfigValueAsync(key).Result with
                        | Error err -> add checks key Fail err
                        | Ok(Some _) -> add checks key Pass "Configured in local git config."
                        | Ok None -> add checks key Warn $"Not set in local git config. Using default: {fallback}"

                    let providerRuntime = AiProviderFactory.createFromSettings runner settings
                    let! providerCall = providerRuntime.ListSessionsAsync()
                    match providerCall with
                    | Ok sessions ->
                        add checks "provider-runtime" Pass $"Provider command works (listed {sessions.Length} sessions)."
                    | Error err ->
                        add checks "provider-runtime" Fail $"Provider command failed: {err}"

            match git.GetRefObjectIdAsync("refs/notes/commits").Result with
            | Error err -> add checks "notes-local-ref" Fail err
            | Ok(Some oid) -> add checks "notes-local-ref" Pass $"Local notes ref exists ({oid})."
            | Ok None -> add checks "notes-local-ref" Warn "Local notes ref refs/notes/commits does not exist yet."

            let fetchKey = $"remote.{remote}.fetch"
            match git.GetLocalConfigValuesAsync(fetchKey).Result with
            | Error err -> add checks "notes-fetch-config" Fail err
            | Ok values ->
                let expected = "+refs/notes/*:refs/notes/*"
                if values |> List.exists (fun value -> value.Trim() = expected) then
                    add checks "notes-fetch-config" Pass $"Found {expected} in {fetchKey}."
                else
                    add checks "notes-fetch-config" Warn $"Missing {expected} in {fetchKey}."

            let! remoteRefsResult = git.ListRemoteRefsAsync(remote, "refs/notes/*")
            match remoteRefsResult with
            | Error err -> add checks "remote-notes" Warn $"Unable to read remote note refs: {err}"
            | Ok refs when List.isEmpty refs -> add checks "remote-notes" Warn $"No refs/notes/* found on remote '{remote}'."
            | Ok refs -> add checks "remote-notes" Pass $"Remote '{remote}' has {refs.Length} notes ref(s)."

            let rewriteKeys =
                [ "notes.rewriteRef", "refs/notes/*"
                  "notes.rewriteMode", "concatenate"
                  "notes.rewrite.rebase", "true"
                  "notes.rewrite.amend", "true" ]

            for key, expected in rewriteKeys do
                match git.GetLocalConfigValueAsync(key).Result with
                | Error err -> add checks key Fail err
                | Ok(Some value) when value.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase) ->
                    add checks key Pass $"Configured as {value}."
                | Ok(Some value) ->
                    add checks key Warn $"Configured as {value}; expected {expected}."
                | Ok None ->
                    add checks key Warn $"Not configured; expected {expected}."

            match outputFormat with
            | "json" -> renderJson remote checks
            | _ -> renderText remote checks

            if hasFailure checks then
                return CommandResult.Failed "Doctor found configuration or runtime problems."
            else
                return CommandResult.Completed
        }
