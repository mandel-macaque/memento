namespace GitMemento

open System

module SessionNotes =
    [<Literal>]
    let EnvelopeMarker = "<!-- git-memento-sessions:v1 -->"

    [<Literal>]
    let NoteVersionHeader = "<!-- git-memento-note-version:1 -->"

    [<Literal>]
    let SessionStartMarker = "<!-- git-memento-session:start -->"

    [<Literal>]
    let SessionEndMarker = "<!-- git-memento-session:end -->"

    let private escapedSessionStartMarker = "\\" + SessionStartMarker
    let private escapedSessionEndMarker = "\\" + SessionEndMarker
    let private escapedEnvelopeMarker = "\\" + EnvelopeMarker
    let private escapedVersionHeaderMarker = "\\" + NoteVersionHeader

    let private escapeCollisionLine (line: string) =
        let trimmed = line.Trim()
        if
            trimmed = SessionStartMarker
            || trimmed = SessionEndMarker
            || trimmed = EnvelopeMarker
            || trimmed = NoteVersionHeader
        then
            line.Replace(trimmed, "\\" + trimmed)
        else
            line

    let private unescapeCollisionLine (line: string) =
        let trimmed = line.Trim()
        if
            trimmed = escapedSessionStartMarker
            || trimmed = escapedSessionEndMarker
            || trimmed = escapedEnvelopeMarker
            || trimmed = escapedVersionHeaderMarker
        then
            line.Replace(trimmed, trimmed.Substring(1))
        else
            line

    let private parseEnvelopeEntries (normalized: string) : string list option =
        let lines = normalized.Split('\n') |> Array.toList
        let firstNonEmpty =
            lines |> List.tryFind (fun line -> not (String.IsNullOrWhiteSpace line))

        match firstNonEmpty with
        | Some line when line.Trim().Equals(EnvelopeMarker, StringComparison.Ordinal) ->
            let entries = ResizeArray<string>()
            let current = ResizeArray<string>()
            let mutable insideSession = false

            for line in lines do
                let trimmed = line.Trim()
                if trimmed.Equals(SessionStartMarker, StringComparison.Ordinal) then
                    current.Clear()
                    insideSession <- true
                elif trimmed.Equals(SessionEndMarker, StringComparison.Ordinal) then
                    if insideSession then
                        let entry =
                            current
                            |> Seq.map unescapeCollisionLine
                            |> String.concat "\n"
                            |> fun value -> value.Trim()

                        if not (String.IsNullOrWhiteSpace entry) then
                            entries.Add(entry)

                    current.Clear()
                    insideSession <- false
                elif insideSession then
                    current.Add(line)

            Some(entries |> Seq.toList)
        | _ -> None

    let parseEntries (note: string) : string list =
        let normalized = note.Replace("\r\n", "\n").Trim()
        if String.IsNullOrWhiteSpace normalized then
            []
        else
            match parseEnvelopeEntries normalized with
            | Some entries when not (List.isEmpty entries) -> entries
            | _ -> [ normalized ]

    let renderEntries (entries: string list) : string =
        let normalizedEntries =
            entries
            |> List.map (fun e -> e.Replace("\r\n", "\n").Trim())
            |> List.filter (String.IsNullOrWhiteSpace >> not)

        match normalizedEntries with
        | [] -> String.Empty
        | values ->
            let blocks =
                values
                |> List.map (fun entry ->
                    let escapedEntry =
                        entry.Split('\n')
                        |> Array.map escapeCollisionLine
                        |> String.concat "\n"
                    $"{SessionStartMarker}\n{escapedEntry}\n{SessionEndMarker}")

            String.concat "\n\n" ([ EnvelopeMarker; NoteVersionHeader ] @ blocks) + Environment.NewLine
