module IntegrationTests

open System
open System.IO
open System.Threading.Tasks
open GitMemento
open Xunit

type private CaptureOutput() =
    let info = ResizeArray<string>()
    let errors = ResizeArray<string>()

    member _.InfoLines = info |> Seq.toList
    member _.ErrorLines = errors |> Seq.toList

    interface IUserOutput with
        member _.Info(message: string) = info.Add(message)
        member _.Error(message: string) = errors.Add(message)

type private EnvScope(values: (string * string option) list) =
    let originals =
        values
        |> List.map (fun (key, _) -> key, Environment.GetEnvironmentVariable(key))

    do
        values
        |> List.iter (fun (key, value) ->
            match value with
            | Some v -> Environment.SetEnvironmentVariable(key, v)
            | None -> Environment.SetEnvironmentVariable(key, null))

    interface IDisposable with
        member _.Dispose() =
            originals
            |> List.iter (fun (key, value) -> Environment.SetEnvironmentVariable(key, value))

let private writeExecutable path content =
    let path: string = path
    let content: string = content
    File.WriteAllText(path, content)
    let chmod = ProcessCommandRunner() :> ICommandRunner
    chmod.RunCaptureAsync("chmod", [ "+x"; path ]).Result |> ignore

let private buildFakeGitScript (stateDir: string) =
    let script =
        """#!/bin/sh
set -eu
state="__STATE_DIR__"
cmd="${1:-}"

if [ "$cmd" = "rev-parse" ]; then
  if [ "${2:-}" = "--is-inside-work-tree" ]; then
    echo true
    exit 0
  fi
  if [ "${2:-}" = "HEAD" ]; then
    if [ -f "$state/head" ]; then
      cat "$state/head"
      exit 0
    fi
    echo "missing HEAD" >&2
    exit 1
  fi
fi

if [ "$cmd" = "config" ]; then
  if [ "${2:-}" = "--get" ] && [ "${3:-}" = "user.name" ]; then
    echo "Test Dev"
    exit 0
  fi
  if [ "${2:-}" = "--get" ] && [ "${3:-}" = "user.email" ]; then
    echo "test@example.com"
    exit 0
  fi
  if [ "${2:-}" = "--get-all" ] && [ "${3:-}" = "remote.origin.fetch" ]; then
    if [ -f "$state/remote-origin-fetch" ]; then
      cat "$state/remote-origin-fetch"
      exit 0
    fi
    exit 1
  fi
  if [ "${2:-}" = "--add" ] && [ "${3:-}" = "remote.origin.fetch" ]; then
    echo "${4:-}" >> "$state/remote-origin-fetch"
    exit 0
  fi
fi

if [ "$cmd" = "commit" ]; then
  msg=""
  if [ "${2:-}" = "-m" ]; then
    msg="${3:-}"
  fi
  echo "$msg" > "$state/commit-message"
  n=0
  if [ -f "$state/counter" ]; then
    n=$(cat "$state/counter")
  fi
  n=$((n + 1))
  echo "$n" > "$state/counter"
  echo "fakehash$n" > "$state/head"
  exit 0
fi

if [ "$cmd" = "notes" ] && [ "${2:-}" = "add" ]; then
  shift
  shift
  note=""
  hash=""
  while [ $# -gt 0 ]; do
    case "$1" in
      -f)
        shift
        ;;
      -m)
        note="$2"
        shift
        shift
        ;;
      *)
        hash="$1"
        shift
        ;;
    esac
  done
  printf "%s" "$note" > "$state/note.md"
  printf "%s" "$hash" > "$state/note-hash"
  exit 0
fi

if [ "$cmd" = "push" ]; then
  echo "$*" > "$state/push-args"
  exit 0
fi

echo "unsupported fake git command: $*" >&2
exit 2
"""

    script.Replace("__STATE_DIR__", stateDir)

let private buildFakeCodexScript =
    """#!/bin/sh
set -eu

if [ "${1:-}" = "sessions" ] && [ "${2:-}" = "get" ] && [ "${4:-}" = "--json" ]; then
  if [ "${3:-}" = "good-session" ]; then
    printf '{"id":"good-session","title":"Sample Session","messages":[{"role":"user","content":"[INFO] Build feature"},{"role":"assistant","content":"Done and tested"}]}'
    exit 0
  fi
  echo "session not found" >&2
  exit 1
fi

if [ "${1:-}" = "sessions" ] && [ "${2:-}" = "list" ] && [ "${3:-}" = "--json" ]; then
  printf '[{"id":"good-session","title":"Sample Session"},{"id":"other-session"}]'
  exit 0
fi

echo "unsupported fake codex command: $*" >&2
exit 2
"""

[<Fact>]
let ``integration commit flow writes note using fake git and codex binaries`` () =
    let temp = Path.Combine(Path.GetTempPath(), $"memento-it-{Guid.NewGuid():N}")
    Directory.CreateDirectory(temp) |> ignore
    let binDir = Path.Combine(temp, "bin")
    let stateDir = Path.Combine(temp, "state")
    Directory.CreateDirectory(binDir) |> ignore
    Directory.CreateDirectory(stateDir) |> ignore

    let gitPath = Path.Combine(binDir, "git")
    let codexPath = Path.Combine(binDir, "codex")
    writeExecutable gitPath (buildFakeGitScript stateDir)
    writeExecutable codexPath buildFakeCodexScript

    let originalPath = Environment.GetEnvironmentVariable("PATH") |> Option.ofObj |> Option.defaultValue ""
    use _env =
        new EnvScope(
            [ "PATH", Some($"{binDir}{Path.PathSeparator}{originalPath}")
              "MEMENTO_AI_PROVIDER", Some("codex")
              "MEMENTO_CODEX_BIN", Some("codex") ]
        )

    let runner = ProcessCommandRunner() :> ICommandRunner
    let git = GitService(runner) :> IGitService
    let providerSettings =
        { Provider = "codex"
          Executable = "codex"
          GetArgs = "sessions get {id} --json"
          ListArgs = "sessions list --json" }
    let provider = AiProviderFactory.createFromSettings runner providerSettings
    let output = CaptureOutput()
    let workflow = CommitWorkflow(git, provider, output :> IUserOutput)

    let result = workflow.ExecuteAsync("good-session", Some("ship it")).Result

    Assert.Equal(CommandResult.Completed, result)
    let note = File.ReadAllText(Path.Combine(stateDir, "note.md"))
    Assert.Contains("# Git Memento Session", note)
    Assert.Contains("### Test Dev", note)
    Assert.Contains("### Codex", note)
    Assert.Contains("Build feature", note)
    Assert.Contains("Done and tested", note)

[<Fact>]
let ``integration share-notes pushes notes and configures fetch mapping`` () =
    let temp = Path.Combine(Path.GetTempPath(), $"memento-it-{Guid.NewGuid():N}")
    Directory.CreateDirectory(temp) |> ignore
    let binDir = Path.Combine(temp, "bin")
    let stateDir = Path.Combine(temp, "state")
    Directory.CreateDirectory(binDir) |> ignore
    Directory.CreateDirectory(stateDir) |> ignore

    let gitPath = Path.Combine(binDir, "git")
    writeExecutable gitPath (buildFakeGitScript stateDir)

    let originalPath = Environment.GetEnvironmentVariable("PATH") |> Option.ofObj |> Option.defaultValue ""
    use _env = new EnvScope([ "PATH", Some($"{binDir}{Path.PathSeparator}{originalPath}") ])

    let runner = ProcessCommandRunner() :> ICommandRunner
    let git = GitService(runner) :> IGitService
    let output = CaptureOutput()
    let workflow = ShareNotesWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("origin").Result

    Assert.Equal(CommandResult.Completed, result)
    let pushArgs = File.ReadAllText(Path.Combine(stateDir, "push-args")).Trim()
    Assert.Equal("push origin refs/notes/*", pushArgs)
    let fetchConfig = File.ReadAllText(Path.Combine(stateDir, "remote-origin-fetch")).Trim()
    Assert.Contains("+refs/notes/*:refs/notes/*", fetchConfig)
