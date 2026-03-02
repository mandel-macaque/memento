namespace GitMemento

module MementoConfig =
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
                            match read $"{keyBase}.summary.bin" defaults.SummaryExecutable with
                            | Error err -> Error err
                            | Ok summaryExecutable ->
                                match read $"{keyBase}.summary.args" defaults.SummaryArgs with
                                | Error err -> Error err
                                | Ok summaryArgs ->
                                    Ok
                                        { Provider = provider
                                          Executable = executable
                                          GetArgs = getArgs
                                          ListArgs = listArgs
                                          SummaryExecutable = summaryExecutable
                                          SummaryArgs = summaryArgs }
