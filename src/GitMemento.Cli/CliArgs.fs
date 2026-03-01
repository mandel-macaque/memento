namespace GitMemento

open System

module CliArgs =
    let usage =
        "Usage:\n"
        + "  git memento init [codex|claude]\n"
        + "  git memento commit <session-id> [-m \"commit message\"]...\n"
        + "  git memento share-notes [remote]\n"
        + "  git memento help"

    let private toNonEmptyOption (value: string) =
        if String.IsNullOrWhiteSpace value then None else Some value

    let parse (args: string array) : Result<Command, string> =
        if args.Length = 0 then
            Ok Command.Help
        else
            match args[0].ToLowerInvariant() with
            | "help"
            | "--help"
            | "-h" -> Ok Command.Help
            | "init" ->
                if args.Length = 1 then
                    Ok(Command.Init(None))
                elif args.Length = 2 then
                    let provider = args[1].Trim()
                    if String.IsNullOrWhiteSpace provider then
                        Error "Provider cannot be empty. Use: git memento init [codex|claude]"
                    else
                        Ok(Command.Init(Some provider))
                else
                    Error "Usage: git memento init [codex|claude]"
            | "commit" ->
                if args.Length < 2 then
                    Error "Missing <session-id>. Usage: git memento commit <session-id> [-m \"commit message\"]..."
                else
                    let sessionId = args[1].Trim()
                    if String.IsNullOrWhiteSpace sessionId then
                        Error "Session id cannot be empty."
                    else
                        let mutable messages: string list = []
                        let mutable i = 2
                        let mutable parseError: string option = None

                        while i < args.Length && parseError.IsNone do
                            let current = args[i]
                            if current = "-m" || current = "--message" then
                                if i + 1 >= args.Length then
                                    parseError <- Some "Flag -m/--message requires a non-empty commit message."
                                else
                                    match toNonEmptyOption args[i + 1] with
                                    | Some message -> messages <- message :: messages
                                    | None -> parseError <- Some "Flag -m/--message requires a non-empty commit message."
                                    i <- i + 2
                            elif current.StartsWith("--message=", StringComparison.Ordinal) then
                                let inlineValue = current.Substring("--message=".Length)
                                match toNonEmptyOption inlineValue with
                                | Some message -> messages <- message :: messages
                                | None -> parseError <- Some "Flag -m/--message requires a non-empty commit message."
                                i <- i + 1
                            elif current.StartsWith("-m", StringComparison.Ordinal) then
                                let inlineValue = current.AsSpan(2).ToString()
                                let normalizedInlineValue =
                                    if inlineValue.StartsWith("=", StringComparison.Ordinal) then
                                        inlineValue.Substring(1)
                                    else
                                        inlineValue

                                match toNonEmptyOption normalizedInlineValue with
                                | Some message -> messages <- message :: messages
                                | None -> parseError <- Some "Flag -m/--message requires a non-empty commit message."
                                i <- i + 1
                            else
                                parseError <- Some $"Unknown argument: {current}"

                        match parseError with
                        | Some err -> Error err
                        | None -> Ok(Command.Commit(sessionId, List.rev messages))
            | "share-notes" ->
                if args.Length = 1 then
                    Ok(Command.ShareNotes("origin"))
                elif args.Length = 2 then
                    let remote = args[1].Trim()
                    if String.IsNullOrWhiteSpace remote then
                        Error "Remote name cannot be empty."
                    else
                        Ok(Command.ShareNotes(remote))
                else
                    Error "Usage: git memento share-notes [remote]"
            | unknown ->
                Error $"Unknown command '{unknown}'.{Environment.NewLine}{usage}"
