namespace GitMemento

open System

module TextCleaning =
    let private prefixes =
        [| "[debug]"
           "[info]"
           "[trace]"
           "[warn]"
           "[warning]"
           "[error]"
           "debug:"
           "info:"
           "trace:"
           "warn:"
           "warning:"
           "error:" |]

    let cleanLine (line: string) =
        if String.IsNullOrEmpty line then
            String.Empty
        else
            let mutable current = line.TrimStart()
            let mutable changed = true

            while changed && not (String.IsNullOrEmpty current) do
                changed <- false
                let span = current.AsSpan()

                for prefix in prefixes do
                    if (not changed) && span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                        current <- span.Slice(prefix.Length).ToString().TrimStart()
                        changed <- true

            current.TrimEnd()

    let cleanBlock (value: string) =
        if String.IsNullOrWhiteSpace value then
            String.Empty
        else
            value.Split('\n')
            |> Array.map (fun l -> l.TrimEnd('\r') |> cleanLine)
            |> String.concat Environment.NewLine
            |> fun text -> text.Trim()
