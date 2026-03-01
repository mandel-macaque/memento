namespace GitMemento

open System

module CliArgs =
    let usage =
        "Usage:\n"
        + "  git memento init [codex|claude]\n"
        + "  git memento commit <session-id> [-m \"commit message\"]...\n"
        + "  git memento amend [session-id] [-m \"commit message\"]...\n"
        + "  git memento push [remote]\n"
        + "  git memento share-notes [remote]\n"
        + "  git memento notes-sync [remote] [--strategy <strategy>]\n"
        + "  git memento notes-rewrite-setup\n"
        + "  git memento notes-carry --onto <commit> --from-range <base>..<head>\n"
        + "  git memento --version\n"
        + "  git memento help"

    module Parsing =
        let inline private isBlank (value: string) = String.IsNullOrWhiteSpace value

        let private toNonEmptyOption (value: string) =
            if isBlank value then None else Some value

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
                Error "Missing <session-id>. Usage: git memento commit <session-id> [-m \"commit message\"]..."
            else
                let sessionId = args[1].Trim()
                if isBlank sessionId then
                    Error "Session id cannot be empty."
                else
                    let rec loop (i: int) (messagesRev: string list) : Result<string list, string> =
                        if i >= args.Length then
                            Ok(List.rev messagesRev)
                        else
                            let current = args[i]
                            if current = "-m" || current = "--message" then
                                if i + 1 >= args.Length then
                                    Error "Flag -m/--message requires a non-empty commit message."
                                else
                                    match toNonEmptyOption args[i + 1] with
                                    | Some message -> loop (i + 2) (message :: messagesRev)
                                    | None -> Error "Flag -m/--message requires a non-empty commit message."
                            elif current.StartsWith("--message=", StringComparison.Ordinal) then
                                let inlineValue = current.Substring("--message=".Length)
                                match toNonEmptyOption inlineValue with
                                | Some message -> loop (i + 1) (message :: messagesRev)
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                            elif current.StartsWith("-m", StringComparison.Ordinal) then
                                let inlineValue = current.AsSpan(2).ToString()
                                let normalizedInlineValue =
                                    if inlineValue.StartsWith("=", StringComparison.Ordinal) then
                                        inlineValue.Substring(1)
                                    else
                                        inlineValue

                                match toNonEmptyOption normalizedInlineValue with
                                | Some message -> loop (i + 1) (message :: messagesRev)
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                            else
                                Error $"Unknown argument: {current}"

                    loop 2 [] |> Result.map (fun messages -> Command.Commit(sessionId, messages))

        let parseAmend (args: string array) : Result<Command, string> =
            let parseMessages (startIndex: int) =
                let rec loop (i: int) (messagesRev: string list) : Result<string list, string> =
                    if i >= args.Length then
                        Ok(List.rev messagesRev)
                    else
                        let current = args[i]
                        if current = "-m" || current = "--message" then
                            if i + 1 >= args.Length then
                                Error "Flag -m/--message requires a non-empty commit message."
                            else
                                match toNonEmptyOption args[i + 1] with
                                | Some message -> loop (i + 2) (message :: messagesRev)
                                | None -> Error "Flag -m/--message requires a non-empty commit message."
                        elif current.StartsWith("--message=", StringComparison.Ordinal) then
                            let inlineValue = current.Substring("--message=".Length)
                            match toNonEmptyOption inlineValue with
                            | Some message -> loop (i + 1) (message :: messagesRev)
                            | None -> Error "Flag -m/--message requires a non-empty commit message."
                        elif current.StartsWith("-m", StringComparison.Ordinal) then
                            let inlineValue = current.AsSpan(2).ToString()
                            let normalizedInlineValue =
                                if inlineValue.StartsWith("=", StringComparison.Ordinal) then
                                    inlineValue.Substring(1)
                                else
                                    inlineValue

                            match toNonEmptyOption normalizedInlineValue with
                            | Some message -> loop (i + 1) (message :: messagesRev)
                            | None -> Error "Flag -m/--message requires a non-empty commit message."
                        else
                            Error $"Unknown argument: {current}"

                loop startIndex []

            if args.Length = 1 then
                Ok(Command.Amend(None, []))
            else
                let firstArg = args[1]
                if firstArg.StartsWith("-", StringComparison.Ordinal) then
                    parseMessages 1 |> Result.map (fun messages -> Command.Amend(None, messages))
                else
                    let sessionId = firstArg.Trim()
                    if isBlank sessionId then
                        Error "Session id cannot be empty."
                    else
                        parseMessages 2 |> Result.map (fun messages -> Command.Amend(Some sessionId, messages))

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
            | "share-notes" -> Parsing.parseShareNotes args
            | "push" -> Parsing.parsePush args
            | "notes-sync" -> Parsing.parseNotesSync args
            | "notes-rewrite-setup" -> Parsing.parseNotesRewriteSetup args
            | "notes-carry" -> Parsing.parseNotesCarry args
            | unknown -> Error $"Unknown command '{unknown}'.{Environment.NewLine}{usage}"
