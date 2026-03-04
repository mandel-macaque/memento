namespace GitMemento

open System

module CliArgs =
    let usage =
        "Usage:\n"
        + "  git memento init [codex|claude]\n"
        + "  git memento commit <session-id> [-m \"commit message\"]... [--summary-skill <skill|default>] [--summary-max-message-chars <n>] [--summary-max-transcript-chars <n>] [--summary-max-prompt-chars <n>] [--summary-require-full-session]\n"
        + "  git memento amend [session-id] [-m \"commit message\"]... [--summary-skill <skill|default>] [--summary-max-message-chars <n>] [--summary-max-transcript-chars <n>] [--summary-max-prompt-chars <n>] [--summary-require-full-session]\n"
        + "  git memento audit [--range <A..B>] [--strict] [--format <text|json>]\n"
        + "  git memento doctor [remote] [--format <text|json>]\n"
        + "  git memento push [remote]\n"
        + "  git memento share-notes [remote]\n"
        + "  git memento notes-sync [remote] [--strategy <strategy>]\n"
        + "  git memento notes-rewrite-setup\n"
        + "  git memento notes-carry --onto <commit> --from-range <base>..<head>\n"
        + "  git memento --version\n"
        + "  git memento help"

    module Parsing =
        let inline private isBlank (value: string) = String.IsNullOrWhiteSpace value
        let private defaultSummaryLimits () =
            { MaxMessageChars = None
              MaxTranscriptChars = None
              MaxPromptChars = None
              RequireFullSession = false }

        let private toNonEmptyOption (value: string) =
            if isBlank value then None else Some value

        let private parsePositiveInt (flagName: string) (raw: string) =
            let value = raw.Trim()
            if isBlank value then
                Error $"Flag {flagName} requires a non-empty value."
            else
                match Int32.TryParse(value) with
                | true, parsed when parsed > 0 -> Ok parsed
                | _ -> Error $"Flag {flagName} requires a positive integer value."

        let private hasSummaryLimitOverrides (limits: SummaryGenerationLimits) =
            limits.MaxMessageChars.IsSome
            || limits.MaxTranscriptChars.IsSome
            || limits.MaxPromptChars.IsSome
            || limits.RequireFullSession

        let private parseOptionalRemote
            (commandName: string)
            (commandCtor: string -> Command)
            (args: string array)
            : Result<Command, string> =
            if args.Length = 1 then
                Ok(commandCtor "origin")
            elif args.Length = 2 then
                let remote = args[1].Trim()
                if isBlank remote then
                    Error "Remote name cannot be empty."
                else
                    Ok(commandCtor remote)
            else
                Error $"Usage: git memento {commandName} [remote]"

        let parseInit (args: string array) : Result<Command, string> =
            if args.Length = 1 then
                Ok(Command.Init(None))
            elif args.Length = 2 then
                let provider = args[1].Trim()
                if isBlank provider then
                    Error "Provider cannot be empty. Use: git memento init [codex|claude]"
                else
                    Ok(Command.Init(Some provider))
            else
                Error "Usage: git memento init [codex|claude]"

        let parseCommit (args: string array) : Result<Command, string> =
            if args.Length < 2 then
                Error
                    "Missing <session-id>. Usage: git memento commit <session-id> [-m \"commit message\"]... [--summary-skill <skill|default>] [--summary-max-message-chars <n>] [--summary-max-transcript-chars <n>] [--summary-max-prompt-chars <n>] [--summary-require-full-session]"
            else
                let sessionId = args[1].Trim()
                if isBlank sessionId then
                    Error "Session id cannot be empty."
                else
                    let rec loop
                        (i: int)
                        (messagesRev: string list)
                        (summarySkill: string option)
                        (summaryLimits: SummaryGenerationLimits)
                        : Result<string list * string option * SummaryGenerationLimits, string> =
                        if i >= args.Length then
                            Ok(List.rev messagesRev, summarySkill, summaryLimits)
                        else
                            let current = args[i]
                            if current = "-m" || current = "--message" then
                                if i + 1 >= args.Length then
                                    Error "Flag -m/--message requires a non-empty commit message."
                                else
                                    match toNonEmptyOption args[i + 1] with
                                    | Some message -> loop (i + 2) (message :: messagesRev) summarySkill summaryLimits
                                    | None -> Error "Flag -m/--message requires a non-empty commit message."
                            elif current.StartsWith("--message=", StringComparison.Ordinal) then
                                let inlineValue = current.Substring("--message=".Length)
                                match toNonEmptyOption inlineValue with
                                | Some message -> loop (i + 1) (message :: messagesRev) summarySkill summaryLimits
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                            elif current = "--summary-skill" then
                                if i + 1 >= args.Length then
                                    Error "Flag --summary-skill requires a non-empty value."
                                else
                                    let value = args[i + 1].Trim()
                                    if isBlank value then
                                        Error "Flag --summary-skill requires a non-empty value."
                                    elif summarySkill.IsSome then
                                        Error "Flag --summary-skill can only be provided once."
                                    else
                                        loop (i + 2) messagesRev (Some value) summaryLimits
                            elif current.StartsWith("--summary-skill=", StringComparison.Ordinal) then
                                let value = current.Substring("--summary-skill=".Length).Trim()
                                if isBlank value then
                                    Error "Flag --summary-skill requires a non-empty value."
                                elif summarySkill.IsSome then
                                    Error "Flag --summary-skill can only be provided once."
                                else
                                    loop (i + 1) messagesRev (Some value) summaryLimits
                            elif current = "--summary-max-message-chars" then
                                if i + 1 >= args.Length then
                                    Error "Flag --summary-max-message-chars requires a value."
                                else
                                    match parsePositiveInt "--summary-max-message-chars" args[i + 1] with
                                    | Error err -> Error err
                                    | Ok value ->
                                        loop
                                            (i + 2)
                                            messagesRev
                                            summarySkill
                                            { summaryLimits with
                                                MaxMessageChars = Some value }
                            elif current.StartsWith("--summary-max-message-chars=", StringComparison.Ordinal) then
                                let value = current.Substring("--summary-max-message-chars=".Length)
                                match parsePositiveInt "--summary-max-message-chars" value with
                                | Error err -> Error err
                                | Ok parsed ->
                                    loop
                                        (i + 1)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxMessageChars = Some parsed }
                            elif current = "--summary-max-transcript-chars" then
                                if i + 1 >= args.Length then
                                    Error "Flag --summary-max-transcript-chars requires a value."
                                else
                                    match parsePositiveInt "--summary-max-transcript-chars" args[i + 1] with
                                    | Error err -> Error err
                                    | Ok value ->
                                        loop
                                            (i + 2)
                                            messagesRev
                                            summarySkill
                                            { summaryLimits with
                                                MaxTranscriptChars = Some value }
                            elif current.StartsWith("--summary-max-transcript-chars=", StringComparison.Ordinal) then
                                let value = current.Substring("--summary-max-transcript-chars=".Length)
                                match parsePositiveInt "--summary-max-transcript-chars" value with
                                | Error err -> Error err
                                | Ok parsed ->
                                    loop
                                        (i + 1)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxTranscriptChars = Some parsed }
                            elif current = "--summary-max-prompt-chars" then
                                if i + 1 >= args.Length then
                                    Error "Flag --summary-max-prompt-chars requires a value."
                                else
                                    match parsePositiveInt "--summary-max-prompt-chars" args[i + 1] with
                                    | Error err -> Error err
                                    | Ok value ->
                                        loop
                                            (i + 2)
                                            messagesRev
                                            summarySkill
                                            { summaryLimits with
                                                MaxPromptChars = Some value }
                            elif current.StartsWith("--summary-max-prompt-chars=", StringComparison.Ordinal) then
                                let value = current.Substring("--summary-max-prompt-chars=".Length)
                                match parsePositiveInt "--summary-max-prompt-chars" value with
                                | Error err -> Error err
                                | Ok parsed ->
                                    loop
                                        (i + 1)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxPromptChars = Some parsed }
                            elif current = "--summary-require-full-session" then
                                loop
                                    (i + 1)
                                    messagesRev
                                    summarySkill
                                    { summaryLimits with
                                        RequireFullSession = true }
                            elif current.StartsWith("-m", StringComparison.Ordinal) then
                                let inlineValue = current.AsSpan(2).ToString()
                                let normalizedInlineValue =
                                    if inlineValue.StartsWith("=", StringComparison.Ordinal) then
                                        inlineValue.Substring(1)
                                    else
                                        inlineValue

                                match toNonEmptyOption normalizedInlineValue with
                                | Some message -> loop (i + 1) (message :: messagesRev) summarySkill summaryLimits
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                            else
                                Error $"Unknown argument: {current}"

                    loop 2 [] None (defaultSummaryLimits ())
                    |> Result.bind (fun (messages, skill, limits) ->
                        if skill.IsNone && hasSummaryLimitOverrides limits then
                            Error
                                "Summary limit flags require --summary-skill. Add --summary-skill <skill|default> to enable summary mode."
                        else
                            Ok(Command.Commit(sessionId, messages, skill, limits)))

        let parseAmend (args: string array) : Result<Command, string> =
            let parseMessages (startIndex: int) =
                let rec loop
                    (i: int)
                    (messagesRev: string list)
                    (summarySkill: string option)
                    (summaryLimits: SummaryGenerationLimits)
                    : Result<string list * string option * SummaryGenerationLimits, string> =
                    if i >= args.Length then
                        Ok(List.rev messagesRev, summarySkill, summaryLimits)
                    else
                        let current = args[i]
                        if current = "-m" || current = "--message" then
                            if i + 1 >= args.Length then
                                Error "Flag -m/--message requires a non-empty commit message."
                            else
                                match toNonEmptyOption args[i + 1] with
                                | Some message -> loop (i + 2) (message :: messagesRev) summarySkill summaryLimits
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                        elif current.StartsWith("--message=", StringComparison.Ordinal) then
                            let inlineValue = current.Substring("--message=".Length)
                            match toNonEmptyOption inlineValue with
                            | Some message -> loop (i + 1) (message :: messagesRev) summarySkill summaryLimits
                            | None -> Error "Flag -m/--message requires a non-empty commit message."
                        elif current = "--summary-skill" then
                            if i + 1 >= args.Length then
                                Error "Flag --summary-skill requires a non-empty value."
                            else
                                let value = args[i + 1].Trim()
                                if isBlank value then
                                    Error "Flag --summary-skill requires a non-empty value."
                                elif summarySkill.IsSome then
                                    Error "Flag --summary-skill can only be provided once."
                                else
                                    loop (i + 2) messagesRev (Some value) summaryLimits
                        elif current.StartsWith("--summary-skill=", StringComparison.Ordinal) then
                            let value = current.Substring("--summary-skill=".Length).Trim()
                            if isBlank value then
                                Error "Flag --summary-skill requires a non-empty value."
                            elif summarySkill.IsSome then
                                Error "Flag --summary-skill can only be provided once."
                            else
                                loop (i + 1) messagesRev (Some value) summaryLimits
                        elif current = "--summary-max-message-chars" then
                            if i + 1 >= args.Length then
                                Error "Flag --summary-max-message-chars requires a value."
                            else
                                match parsePositiveInt "--summary-max-message-chars" args[i + 1] with
                                | Error err -> Error err
                                | Ok value ->
                                    loop
                                        (i + 2)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxMessageChars = Some value }
                        elif current.StartsWith("--summary-max-message-chars=", StringComparison.Ordinal) then
                            let value = current.Substring("--summary-max-message-chars=".Length)
                            match parsePositiveInt "--summary-max-message-chars" value with
                            | Error err -> Error err
                            | Ok parsed ->
                                loop
                                    (i + 1)
                                    messagesRev
                                    summarySkill
                                    { summaryLimits with
                                        MaxMessageChars = Some parsed }
                        elif current = "--summary-max-transcript-chars" then
                            if i + 1 >= args.Length then
                                Error "Flag --summary-max-transcript-chars requires a value."
                            else
                                match parsePositiveInt "--summary-max-transcript-chars" args[i + 1] with
                                | Error err -> Error err
                                | Ok value ->
                                    loop
                                        (i + 2)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxTranscriptChars = Some value }
                        elif current.StartsWith("--summary-max-transcript-chars=", StringComparison.Ordinal) then
                            let value = current.Substring("--summary-max-transcript-chars=".Length)
                            match parsePositiveInt "--summary-max-transcript-chars" value with
                            | Error err -> Error err
                            | Ok parsed ->
                                loop
                                    (i + 1)
                                    messagesRev
                                    summarySkill
                                    { summaryLimits with
                                        MaxTranscriptChars = Some parsed }
                        elif current = "--summary-max-prompt-chars" then
                            if i + 1 >= args.Length then
                                Error "Flag --summary-max-prompt-chars requires a value."
                            else
                                match parsePositiveInt "--summary-max-prompt-chars" args[i + 1] with
                                | Error err -> Error err
                                | Ok value ->
                                    loop
                                        (i + 2)
                                        messagesRev
                                        summarySkill
                                        { summaryLimits with
                                            MaxPromptChars = Some value }
                        elif current.StartsWith("--summary-max-prompt-chars=", StringComparison.Ordinal) then
                            let value = current.Substring("--summary-max-prompt-chars=".Length)
                            match parsePositiveInt "--summary-max-prompt-chars" value with
                            | Error err -> Error err
                            | Ok parsed ->
                                loop
                                    (i + 1)
                                    messagesRev
                                    summarySkill
                                    { summaryLimits with
                                        MaxPromptChars = Some parsed }
                        elif current = "--summary-require-full-session" then
                            loop
                                (i + 1)
                                messagesRev
                                summarySkill
                                { summaryLimits with
                                    RequireFullSession = true }
                        elif current.StartsWith("-m", StringComparison.Ordinal) then
                            let inlineValue = current.AsSpan(2).ToString()
                            let normalizedInlineValue =
                                if inlineValue.StartsWith("=", StringComparison.Ordinal) then
                                    inlineValue.Substring(1)
                                else
                                    inlineValue

                            match toNonEmptyOption normalizedInlineValue with
                            | Some message -> loop (i + 1) (message :: messagesRev) summarySkill summaryLimits
                            | None -> Error "Flag -m/--message requires a non-empty commit message."
                        else
                            Error $"Unknown argument: {current}"

                loop startIndex [] None (defaultSummaryLimits ())

            if args.Length = 1 then
                Ok(Command.Amend(None, [], None, defaultSummaryLimits ()))
            else
                let firstArg = args[1]
                if firstArg.StartsWith("-", StringComparison.Ordinal) then
                    parseMessages 1
                    |> Result.bind (fun (messages, skill, limits) ->
                        if skill.IsSome then
                            Error "Flag --summary-skill requires an explicit session id with amend."
                        elif hasSummaryLimitOverrides limits then
                            Error
                                "Summary limit flags require --summary-skill with an explicit amend session id."
                        else
                            Ok(Command.Amend(None, messages, None, defaultSummaryLimits ())))
                else
                    let sessionId = firstArg.Trim()
                    if isBlank sessionId then
                        Error "Session id cannot be empty."
                    else
                        parseMessages 2
                        |> Result.bind (fun (messages, skill, limits) ->
                            if skill.IsNone && hasSummaryLimitOverrides limits then
                                Error
                                    "Summary limit flags require --summary-skill. Add --summary-skill <skill|default> to enable summary mode."
                            else
                                Ok(Command.Amend(Some sessionId, messages, skill, limits)))

        let private parseFormatValue (value: string) =
            let normalized = value.Trim().ToLowerInvariant()
            match normalized with
            | "text"
            | "json" -> Ok normalized
            | _ -> Error "Flag --format must be either 'text' or 'json'."

        let parseAudit (args: string array) : Result<Command, string> =
            let rec loop
                (i: int)
                (range: string option)
                (strict: bool)
                (outputFormat: string)
                : Result<Command, string> =
                if i >= args.Length then
                    Ok(Command.Audit(range, strict, outputFormat))
                else
                    let current = args[i]
                    if current = "--range" then
                        if i + 1 >= args.Length then
                            Error "Flag --range requires a value like <base>..<head>."
                        else
                            let value = args[i + 1].Trim()
                            if isBlank value then
                                Error "Flag --range requires a non-empty value."
                            else
                                loop (i + 2) (Some value) strict outputFormat
                    elif current.StartsWith("--range=", StringComparison.Ordinal) then
                        let value = current.Substring("--range=".Length).Trim()
                        if isBlank value then
                            Error "Flag --range requires a non-empty value."
                        else
                            loop (i + 1) (Some value) strict outputFormat
                    elif current = "--strict" then
                        loop (i + 1) range true outputFormat
                    elif current = "--format" then
                        if i + 1 >= args.Length then
                            Error "Flag --format requires a value."
                        else
                            match parseFormatValue args[i + 1] with
                            | Ok formatValue -> loop (i + 2) range strict formatValue
                            | Error err -> Error err
                    elif current.StartsWith("--format=", StringComparison.Ordinal) then
                        match parseFormatValue (current.Substring("--format=".Length)) with
                        | Ok formatValue -> loop (i + 1) range strict formatValue
                        | Error err -> Error err
                    else
                        Error $"Unknown argument: {current}"

            loop 1 None false "text"

        let parseDoctor (args: string array) : Result<Command, string> =
            let rec loop (i: int) (remote: string option) (outputFormat: string) : Result<Command, string> =
                if i >= args.Length then
                    Ok(Command.Doctor(remote |> Option.defaultValue "origin", outputFormat))
                else
                    let current = args[i]
                    if current = "--format" then
                        if i + 1 >= args.Length then
                            Error "Flag --format requires a value."
                        else
                            match parseFormatValue args[i + 1] with
                            | Ok formatValue -> loop (i + 2) remote formatValue
                            | Error err -> Error err
                    elif current.StartsWith("--format=", StringComparison.Ordinal) then
                        match parseFormatValue (current.Substring("--format=".Length)) with
                        | Ok formatValue -> loop (i + 1) remote formatValue
                        | Error err -> Error err
                    elif current.StartsWith("-", StringComparison.Ordinal) then
                        Error $"Unknown argument: {current}"
                    else
                        let remoteValue = current.Trim()
                        if isBlank remoteValue then
                            Error "Remote name cannot be empty."
                        elif remote.IsSome then
                            Error "Usage: git memento doctor [remote] [--format <text|json>]"
                        else
                            loop (i + 1) (Some remoteValue) outputFormat

            loop 1 None "text"

        let parseShareNotes (args: string array) : Result<Command, string> =
            parseOptionalRemote "share-notes" Command.ShareNotes args

        let parsePush (args: string array) : Result<Command, string> =
            parseOptionalRemote "push" Command.Push args

        let parseNotesSync (args: string array) : Result<Command, string> =
            let rec loop (i: int) (remote: string option) (strategy: string) : Result<Command, string> =
                if i >= args.Length then
                    Ok(Command.NotesSync(remote |> Option.defaultValue "origin", strategy))
                else
                    let current = args[i]
                    if current = "--strategy" then
                        if i + 1 >= args.Length then
                            Error "Flag --strategy requires a value."
                        else
                            let strategyValue = args[i + 1].Trim()
                            if isBlank strategyValue then
                                Error "Flag --strategy requires a non-empty value."
                            else
                                loop (i + 2) remote strategyValue
                    elif current.StartsWith("--strategy=", StringComparison.Ordinal) then
                        let strategyValue = current.Substring("--strategy=".Length).Trim()
                        if isBlank strategyValue then
                            Error "Flag --strategy requires a non-empty value."
                        else
                            loop (i + 1) remote strategyValue
                    elif current.StartsWith("-", StringComparison.Ordinal) then
                        Error $"Unknown argument: {current}"
                    else
                        let remoteValue = current.Trim()
                        if isBlank remoteValue then
                            Error "Remote name cannot be empty."
                        elif remote.IsSome then
                            Error "Usage: git memento notes-sync [remote] [--strategy <strategy>]"
                        else
                            loop (i + 1) (Some remoteValue) strategy

            loop 1 None "cat_sort_uniq"

        let parseNotesRewriteSetup (args: string array) : Result<Command, string> =
            if args.Length = 1 then
                Ok Command.NotesRewriteSetup
            else
                Error "Usage: git memento notes-rewrite-setup"

        let parseNotesCarry (args: string array) : Result<Command, string> =
            let rec loop (i: int) (onto: string option) (fromRange: string option) : Result<Command, string> =
                if i >= args.Length then
                    match onto, fromRange with
                    | Some ontoValue, Some rangeValue -> Ok(Command.NotesCarry(ontoValue, rangeValue))
                    | _ -> Error "Usage: git memento notes-carry --onto <commit> --from-range <base>..<head>"
                else
                    let current = args[i]
                    if current = "--onto" then
                        if i + 1 >= args.Length then
                            Error "Flag --onto requires a commit."
                        else
                            let value = args[i + 1].Trim()
                            if isBlank value then
                                Error "Flag --onto requires a non-empty commit."
                            else
                                loop (i + 2) (Some value) fromRange
                    elif current.StartsWith("--onto=", StringComparison.Ordinal) then
                        let value = current.Substring("--onto=".Length).Trim()
                        if isBlank value then
                            Error "Flag --onto requires a non-empty commit."
                        else
                            loop (i + 1) (Some value) fromRange
                    elif current = "--from-range" then
                        if i + 1 >= args.Length then
                            Error "Flag --from-range requires a value like <base>..<head>."
                        else
                            let value = args[i + 1].Trim()
                            if isBlank value then
                                Error "Flag --from-range requires a non-empty value."
                            else
                                loop (i + 2) onto (Some value)
                    elif current.StartsWith("--from-range=", StringComparison.Ordinal) then
                        let value = current.Substring("--from-range=".Length).Trim()
                        if isBlank value then
                            Error "Flag --from-range requires a non-empty value."
                        else
                            loop (i + 1) onto (Some value)
                    else
                        Error $"Unknown argument: {current}"

            loop 1 None None

    let parse (args: string array) : Result<Command, string> =
        if args.Length = 0 then
            Ok Command.Help
        else
            match args[0].ToLowerInvariant() with
            | "help"
            | "--help"
            | "-h" -> Ok Command.Help
            | "version"
            | "--version"
            | "-v" -> Ok Command.Version
            | "init" -> Parsing.parseInit args
            | "commit" -> Parsing.parseCommit args
            | "amend" -> Parsing.parseAmend args
            | "audit" -> Parsing.parseAudit args
            | "doctor" -> Parsing.parseDoctor args
            | "share-notes" -> Parsing.parseShareNotes args
            | "push" -> Parsing.parsePush args
            | "notes-sync" -> Parsing.parseNotesSync args
            | "notes-rewrite-setup" -> Parsing.parseNotesRewriteSetup args
            | "notes-carry" -> Parsing.parseNotesCarry args
            | unknown -> Error $"Unknown command '{unknown}'.{Environment.NewLine}{usage}"
