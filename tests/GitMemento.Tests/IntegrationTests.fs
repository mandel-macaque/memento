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
  if [ "${2:-}" = "--verify" ] && [ "${3:-}" = "--quiet" ]; then
    ref="${4:-}"
    case "$ref" in
      refs/notes/commits)
        if [ -f "$state/ref-refs-notes-commits" ]; then
          cat "$state/ref-refs-notes-commits"
          exit 0
        fi
        exit 1
        ;;
      refs/notes/remote/origin/commits)
        if [ -f "$state/ref-refs-notes-remote-origin-commits" ]; then
          cat "$state/ref-refs-notes-remote-origin-commits"
          exit 0
        fi
        exit 1
        ;;
    esac
    exit 1
  fi
  if [ "${2:-}" = "--verify" ]; then
    rev="${3:-}"
    if [ "$rev" = "squash-target^{commit}" ]; then
      echo "resolved-squash-target"
      exit 0
    fi
    echo "unknown revision: $rev" >&2
    exit 1
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
  if [ "${2:-}" = "--local" ]; then
    key="${3:-}"
    value="${4:-}"
    safe_key=$(echo "$key" | tr '.-' '__')
    printf "%s" "$value" > "$state/local-config-$safe_key"
    exit 0
  fi
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

if [ "$cmd" = "update-ref" ]; then
  ref="${2:-}"
  oid="${3:-}"
  if [ "$ref" = "refs/notes/commits" ]; then
    printf "%s" "$oid" > "$state/ref-refs-notes-commits"
  fi
  case "$ref" in
    refs/notes/memento-backups/*)
      printf "%s" "$oid" > "$state/backup-ref-oid"
      printf "%s" "$ref" > "$state/backup-ref-name"
      ;;
  esac
  exit 0
fi

if [ "$cmd" = "fetch" ]; then
  remote="${2:-}"
  spec="${3:-}"
  echo "$remote $spec" > "$state/fetch-args"
  # Simulate remote notes arriving in namespaced ref.
  if [ "$spec" = "refs/notes/*:refs/notes/remote/$remote/*" ]; then
    if [ -f "$state/remote-notes-oid" ]; then
      cat "$state/remote-notes-oid" > "$state/ref-refs-notes-remote-origin-commits"
    else
      echo "remote-notes-oid" > "$state/ref-refs-notes-remote-origin-commits"
    fi
  fi
  exit 0
fi

if [ "$cmd" = "rev-list" ] && [ "${2:-}" = "--reverse" ]; then
  range="${3:-}"
  if [ "$range" = "main..feature" ]; then
    printf "c1\nc2\nc3\n"
    exit 0
  fi
  exit 0
fi

if [ "$cmd" = "commit" ]; then
  : > "$state/commit-messages"
  amend_mode="false"
  shift
  while [ $# -gt 0 ]; do
    if [ "$1" = "--amend" ]; then
      amend_mode="true"
      shift
    elif [ "$1" = "-m" ]; then
      if [ $# -lt 2 ]; then
        echo "missing message for -m" >&2
        exit 2
      fi
      printf "%s\n" "$2" >> "$state/commit-messages"
      shift
      shift
    else
      shift
    fi
  done
  n=0
  if [ -f "$state/counter" ]; then
    n=$(cat "$state/counter")
  fi
  n=$((n + 1))
  echo "$n" > "$state/counter"
  printf "%s" "$amend_mode" > "$state/commit-amend-mode"
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

if [ "$cmd" = "notes" ] && [ "${2:-}" = "--ref" ] && [ "${4:-}" = "add" ]; then
  notes_ref="${3:-}"
  shift
  shift
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
  safe_ref=$(echo "$notes_ref" | tr '/.-' '___')
  printf "%s" "$note" > "$state/note-$safe_ref.md"
  printf "%s" "$hash" > "$state/note-$safe_ref-hash"
  exit 0
fi

if [ "$cmd" = "notes" ] && [ "${2:-}" = "show" ]; then
  hash="${3:-}"
  if [ -f "$state/note-hash" ] && [ -f "$state/note.md" ]; then
    current_hash=$(cat "$state/note-hash")
    if [ "$hash" = "$current_hash" ]; then
      cat "$state/note.md"
      exit 0
    fi
  fi
  if [ "$hash" = "c1" ]; then
    printf "source note one"
    exit 0
  fi
  if [ "$hash" = "c2" ]; then
    printf "source note two"
    exit 0
  fi
  exit 1
fi

if [ "$cmd" = "notes" ] && [ "${2:-}" = "--ref" ] && [ "${4:-}" = "show" ]; then
  notes_ref="${3:-}"
  hash="${5:-}"
  safe_ref=$(echo "$notes_ref" | tr '/.-' '___')
  if [ -f "$state/note-$safe_ref-hash" ] && [ -f "$state/note-$safe_ref.md" ]; then
    current_hash=$(cat "$state/note-$safe_ref-hash")
    if [ "$hash" = "$current_hash" ]; then
      cat "$state/note-$safe_ref.md"
      exit 0
    fi
  fi
  exit 1
fi

if [ "$cmd" = "notes" ] && [ "${2:-}" = "append" ]; then
  shift
  shift
  note=""
  hash=""
  while [ $# -gt 0 ]; do
    case "$1" in
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
  printf "%s" "$hash" > "$state/append-note-hash"
  printf "%s" "$note" > "$state/append-note.md"
  exit 0
fi

if [ "$cmd" = "notes" ] && [ "${2:-}" = "--ref" ] && [ "${4:-}" = "merge" ]; then
  # git notes --ref <target-ref> merge -s <strategy> <source-ref>
  target_ref="${3:-}"
  strategy="${6:-}"
  source_ref="${7:-}"
  printf "%s" "$target_ref" > "$state/notes-merge-target-ref"
  printf "%s" "$strategy" > "$state/notes-merge-strategy"
  printf "%s" "$source_ref" > "$state/notes-merge-source-ref"
  exit 0
fi

if [ "$cmd" = "push" ]; then
  echo "$*" >> "$state/push-log"
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
  if [ "${3:-}" = "amend-session" ]; then
    printf '{"id":"amend-session","title":"Amend Session","messages":[{"role":"user","content":"Refine commit message"},{"role":"assistant","content":"Applied follow-up changes"}]}'
    exit 0
  fi
  echo "session not found" >&2
  exit 1
fi

if [ "${1:-}" = "sessions" ] && [ "${2:-}" = "list" ] && [ "${3:-}" = "--json" ]; then
  printf '[{"id":"good-session","title":"Sample Session"},{"id":"amend-session","title":"Amend Session"},{"id":"other-session"}]'
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
          ListArgs = "sessions list --json"
          SummaryExecutable = "codex"
          SummaryArgs = "exec \"{prompt}\"" }
    let provider = AiProviderFactory.createFromSettings runner providerSettings
    let output = CaptureOutput()
    let workflow = CommitWorkflow(git, provider, output :> IUserOutput)

    let result = workflow.ExecuteAsync("good-session", [ "ship it" ], None).Result

    Assert.Equal(CommandResult.Completed, result)
    let commitMessages = File.ReadAllLines(Path.Combine(stateDir, "commit-messages"))
    Assert.Equal<string array>([| "ship it" |], commitMessages)
    let note = File.ReadAllText(Path.Combine(stateDir, "note.md"))
    Assert.Contains("# Git Memento Session", note)
    Assert.Contains("### Test Dev", note)
    Assert.Contains("### Codex", note)
    Assert.Contains("Build feature", note)
    Assert.Contains("Done and tested", note)

[<Fact>]
let ``integration commit flow forwards multiple -m message parts`` () =
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
          ListArgs = "sessions list --json"
          SummaryExecutable = "codex"
          SummaryArgs = "exec \"{prompt}\"" }
    let provider = AiProviderFactory.createFromSettings runner providerSettings
    let output = CaptureOutput()
    let workflow = CommitWorkflow(git, provider, output :> IUserOutput)

    let result = workflow.ExecuteAsync("good-session", [ "subject"; "body paragraph" ], None).Result

    Assert.Equal(CommandResult.Completed, result)
    let commitMessages = File.ReadAllLines(Path.Combine(stateDir, "commit-messages"))
    Assert.Equal<string array>([| "subject"; "body paragraph" |], commitMessages)

[<Fact>]
let ``integration amend flow copies existing note when no new session is provided`` () =
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
          ListArgs = "sessions list --json"
          SummaryExecutable = "codex"
          SummaryArgs = "exec \"{prompt}\"" }
    let provider = AiProviderFactory.createFromSettings runner providerSettings
    let output = CaptureOutput()
    let commitWorkflow = CommitWorkflow(git, provider, output :> IUserOutput)

    let commitResult = commitWorkflow.ExecuteAsync("good-session", [ "initial" ], None).Result
    Assert.Equal(CommandResult.Completed, commitResult)

    let amendWorkflow = AmendWorkflow(git, None, output :> IUserOutput)
    let amendResult = amendWorkflow.ExecuteAsync(None, [ "amended" ], None).Result

    Assert.Equal(CommandResult.Completed, amendResult)
    Assert.Equal("true", File.ReadAllText(Path.Combine(stateDir, "commit-amend-mode")))
    let noteHash = File.ReadAllText(Path.Combine(stateDir, "note-hash")).Trim()
    Assert.Equal("fakehash2", noteHash)
    let note = File.ReadAllText(Path.Combine(stateDir, "note.md"))
    Assert.Contains(SessionNotes.EnvelopeMarker, note)
    Assert.Contains("- Session ID: good-session", note)

[<Fact>]
let ``integration amend flow appends a new session to copied note`` () =
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
          ListArgs = "sessions list --json"
          SummaryExecutable = "codex"
          SummaryArgs = "exec \"{prompt}\"" }
    let provider = AiProviderFactory.createFromSettings runner providerSettings
    let output = CaptureOutput()
    let commitWorkflow = CommitWorkflow(git, provider, output :> IUserOutput)

    let commitResult = commitWorkflow.ExecuteAsync("good-session", [ "initial" ], None).Result
    Assert.Equal(CommandResult.Completed, commitResult)

    let amendWorkflow = AmendWorkflow(git, Some provider, output :> IUserOutput)
    let amendResult = amendWorkflow.ExecuteAsync(Some "amend-session", [ "amended" ], None).Result

    Assert.Equal(CommandResult.Completed, amendResult)
    let note = File.ReadAllText(Path.Combine(stateDir, "note.md"))
    let entries = SessionNotes.parseEntries note
    Assert.Equal(2, entries.Length)
    Assert.Contains("- Session ID: good-session", entries[0])
    Assert.Contains("- Session ID: amend-session", entries[1])

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

[<Fact>]
let ``integration push workflow pushes branch then notes`` () =
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
    let workflow = PushWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("origin").Result

    Assert.Equal(CommandResult.Completed, result)
    let pushLog = File.ReadAllLines(Path.Combine(stateDir, "push-log"))
    Assert.Equal<string array>([| "push origin"; "push origin refs/notes/*" |], pushLog)
    let fetchConfig = File.ReadAllText(Path.Combine(stateDir, "remote-origin-fetch")).Trim()
    Assert.Contains("+refs/notes/*:refs/notes/*", fetchConfig)

[<Fact>]
let ``integration notes-sync workflow backs up merges and shares notes`` () =
    let temp = Path.Combine(Path.GetTempPath(), $"memento-it-{Guid.NewGuid():N}")
    Directory.CreateDirectory(temp) |> ignore
    let binDir = Path.Combine(temp, "bin")
    let stateDir = Path.Combine(temp, "state")
    Directory.CreateDirectory(binDir) |> ignore
    Directory.CreateDirectory(stateDir) |> ignore

    File.WriteAllText(Path.Combine(stateDir, "ref-refs-notes-commits"), "local-notes-oid")
    File.WriteAllText(Path.Combine(stateDir, "remote-notes-oid"), "remote-notes-oid")

    let gitPath = Path.Combine(binDir, "git")
    writeExecutable gitPath (buildFakeGitScript stateDir)

    let originalPath = Environment.GetEnvironmentVariable("PATH") |> Option.ofObj |> Option.defaultValue ""
    use _env = new EnvScope([ "PATH", Some($"{binDir}{Path.PathSeparator}{originalPath}") ])

    let runner = ProcessCommandRunner() :> ICommandRunner
    let git = GitService(runner) :> IGitService
    let output = CaptureOutput()
    let workflow = NotesSyncWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("origin", "cat_sort_uniq").Result

    Assert.Equal(CommandResult.Completed, result)
    let fetchArgs = File.ReadAllText(Path.Combine(stateDir, "fetch-args")).Trim()
    Assert.Equal("origin refs/notes/*:refs/notes/remote/origin/*", fetchArgs)
    let mergeStrategy = File.ReadAllText(Path.Combine(stateDir, "notes-merge-strategy")).Trim()
    Assert.Equal("cat_sort_uniq", mergeStrategy)
    let mergeTarget = File.ReadAllText(Path.Combine(stateDir, "notes-merge-target-ref")).Trim()
    Assert.Equal("refs/notes/commits", mergeTarget)
    let mergeSource = File.ReadAllText(Path.Combine(stateDir, "notes-merge-source-ref")).Trim()
    Assert.Equal("refs/notes/remote/origin/commits", mergeSource)
    let pushLog = File.ReadAllLines(Path.Combine(stateDir, "push-log"))
    Assert.Equal<string array>([| "push origin refs/notes/*" |], pushLog)
    let backupOid = File.ReadAllText(Path.Combine(stateDir, "backup-ref-oid")).Trim()
    Assert.Equal("local-notes-oid", backupOid)

[<Fact>]
let ``integration notes-rewrite-setup stores rewrite config in local git metadata`` () =
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
    let workflow = NotesRewriteSetupWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync().Result

    Assert.Equal(CommandResult.Completed, result)
    Assert.Equal("refs/notes/*", File.ReadAllText(Path.Combine(stateDir, "local-config-notes_rewriteRef")))
    Assert.Equal("concatenate", File.ReadAllText(Path.Combine(stateDir, "local-config-notes_rewriteMode")))
    Assert.Equal("true", File.ReadAllText(Path.Combine(stateDir, "local-config-notes_rewrite_rebase")))
    Assert.Equal("true", File.ReadAllText(Path.Combine(stateDir, "local-config-notes_rewrite_amend")))

[<Fact>]
let ``integration notes-carry appends source notes to target commit`` () =
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
    let workflow = NotesCarryWorkflow(git, output :> IUserOutput)

    let result = workflow.ExecuteAsync("squash-target", "main..feature").Result

    Assert.Equal(CommandResult.Completed, result)
    let appendedHash = File.ReadAllText(Path.Combine(stateDir, "append-note-hash")).Trim()
    Assert.Equal("resolved-squash-target", appendedHash)
    let appendedNote = File.ReadAllText(Path.Combine(stateDir, "append-note.md"))
    Assert.Contains("# Git Memento Carried Notes", appendedNote)
    Assert.Contains("## Source Commit c1", appendedNote)
    Assert.Contains("source note one", appendedNote)
    Assert.Contains("## Source Commit c2", appendedNote)
    Assert.Contains("source note two", appendedNote)
