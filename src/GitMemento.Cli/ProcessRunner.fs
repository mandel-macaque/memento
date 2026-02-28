namespace GitMemento

open System
open System.Diagnostics
open System.Text
open System.Threading.Tasks

type ProcessResult =
    { ExitCode: int
      StdOut: string
      StdErr: string }

type ICommandRunner =
    abstract member RunCaptureAsync: fileName: string * arguments: string list -> Task<ProcessResult>
    abstract member RunStreamingAsync: fileName: string * arguments: string list -> Task<int>

type ProcessCommandRunner() =
    let makeStartInfo (fileName: string) (arguments: string list) =
        let info = ProcessStartInfo(fileName)
        info.UseShellExecute <- false
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.CreateNoWindow <- true
        for arg in arguments do
            info.ArgumentList.Add(arg)
        info

    interface ICommandRunner with
        member _.RunCaptureAsync(fileName: string, arguments: string list) =
            task {
                let info = makeStartInfo fileName arguments
                use proc = new Process(StartInfo = info)
                let outBuilder = StringBuilder(1024)
                let errBuilder = StringBuilder(512)

                proc.OutputDataReceived.Add(fun evt ->
                    if not (isNull evt.Data) then
                        outBuilder.AppendLine(evt.Data) |> ignore)

                proc.ErrorDataReceived.Add(fun evt ->
                    if not (isNull evt.Data) then
                        errBuilder.AppendLine(evt.Data) |> ignore)

                if not (proc.Start()) then
                    return
                        { ExitCode = 1
                          StdOut = String.Empty
                          StdErr = $"Unable to start process: {fileName}" }
                else
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()
                    do! proc.WaitForExitAsync()

                    return
                        { ExitCode = proc.ExitCode
                          StdOut = outBuilder.ToString().TrimEnd()
                          StdErr = errBuilder.ToString().TrimEnd() }
            }

        member _.RunStreamingAsync(fileName: string, arguments: string list) =
            task {
                let info = ProcessStartInfo(fileName)
                info.UseShellExecute <- false
                info.RedirectStandardOutput <- false
                info.RedirectStandardError <- false
                info.RedirectStandardInput <- false
                info.CreateNoWindow <- false
                for arg in arguments do
                    info.ArgumentList.Add(arg)

                use proc = new Process(StartInfo = info)
                if not (proc.Start()) then
                    return 1
                else
                    do! proc.WaitForExitAsync()
                    return proc.ExitCode
            }

module CommandLine =
    let splitArgs (value: string) =
        let tokens = ResizeArray<string>()
        let buffer = StringBuilder()
        let mutable inQuotes = false
        let mutable quoteChar = '\000'

        let flush () =
            if buffer.Length > 0 then
                tokens.Add(buffer.ToString())
                buffer.Clear() |> ignore

        for c in value do
            if inQuotes then
                if c = quoteChar then
                    inQuotes <- false
                    quoteChar <- '\000'
                else
                    buffer.Append(c) |> ignore
            else
                match c with
                | '\'' ->
                    inQuotes <- true
                    quoteChar <- c
                | '"' ->
                    inQuotes <- true
                    quoteChar <- c
                | ' '
                | '\t' ->
                    flush ()
                | _ -> buffer.Append(c) |> ignore

        flush ()
        tokens |> Seq.toList
