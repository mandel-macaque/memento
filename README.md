# git-memento

`git-memento` is a Git extension that records the AI coding session used to produce a commit.

It runs a commit and then stores a cleaned markdown conversation as a git note on the new commit.

## Goal

- Create commits with normal Git flow (`-m` or editor).
- Attach the AI session trace to the commit (`git notes`).
- Keep provider support extensible (Codex first, others later).
- Produce human-readable markdown notes.

## Command

Initialize per-repository memento settings:

```bash
git memento init
git memento init codex
git memento init claude
```

`init` stores configuration in local git metadata (`.git/config`) under `memento.*`.

```bash
git memento commit <session-id> -m "Normal commit message"
git memento commit <session-id> -m "Subject line" -m "Body paragraph"
```

Or:

```bash
git memento commit <session-id>
```

You can pass `-m` multiple times, and each value is forwarded to `git commit` in order.
When `-m` is omitted, `git commit` opens your default editor.

Share notes with the repository remote (default: `origin`):

```bash
git memento share-notes
git memento share-notes upstream
```

This pushes `refs/notes/*` and configures local `remote.<name>.fetch` so notes can be fetched by teammates.

Show command help:

```bash
git memento help
```

## Provider Configuration

Provider defaults can come from env vars, and `init` persists the selected provider + values in local git config:

- `MEMENTO_AI_PROVIDER` (default: `codex`)
- `MEMENTO_CODEX_BIN` (default: `codex`)
- `MEMENTO_CODEX_GET_ARGS` (default: `sessions get {id} --json`)
- `MEMENTO_CODEX_LIST_ARGS` (default: `sessions list --json`)
- `MEMENTO_CLAUDE_BIN` (default: `claude`)
- `MEMENTO_CLAUDE_GET_ARGS` (default: `sessions get {id} --json`)
- `MEMENTO_CLAUDE_LIST_ARGS` (default: `sessions list --json`)

Set `MEMENTO_AI_PROVIDER=claude` to use Claude Code.

Runtime behavior:
- If the repository is not configured yet, `commit` and `share-notes` fail with a message to run `git memento init` first.
- Stored git metadata keys include:
  - `memento.provider`
  - `memento.codex.bin`, `memento.codex.getArgs`, `memento.codex.listArgs`
  - `memento.claude.bin`, `memento.claude.getArgs`, `memento.claude.listArgs`

If a session id is not found, `git-memento` asks Codex for available sessions and prints them.

## Build (AOT)

Requires `.NET SDK 10` and native toolchain dependencies for NativeAOT.

### macOS

```bash
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r osx-arm64 -p:PublishAot=true
```

### Linux

```bash
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r linux-x64 -p:PublishAot=true
```

### Windows (PowerShell)

```powershell
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r win-x64 -p:PublishAot=true
```

## Local Install as Git Tool

Git discovers commands named `git-<name>` in `PATH`.

1. Publish for your platform.
2. Copy the produced executable to a directory in your `PATH`.
3. Ensure the binary name is `git-memento` (or `git-memento.exe` on Windows).

Then run:

```bash
git memento commit <session-id> -m "message"
```

## Curl Install

Install from latest GitHub release:

```bash
curl -fsSL https://raw.githubusercontent.com/mandel-macaque/memento/main/install.sh | sh
```

## Test

```bash
dotnet test GitMemento.slnx
```

## Commit Note Comments (GitHub Action)

This repository includes a reusable action that reads `git notes` created by `git-memento` and posts a commit comment with:

- The AI agent used (provider + session id when available).
- The attached note body in a collapsible `<details>` block.

Action definition:

- `action.yml` at repository root.

Example workflow:

```yaml
name: memento-note-comments

on:
  push:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: write
  pull-requests: read

jobs:
  comment-memento-notes:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: mandel-macaque/memento@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

Inputs:

- `github-token` (default: `${{ github.token }}`)
- `notes-fetch-refspec` (default: `refs/notes/*:refs/notes/*`)
- `max-comment-length` (default: `65000`)

Local workflow in this repository:

- `.github/workflows/memento-note-comments.yml`

### Publish This Action To GitHub Marketplace

1. Ensure `action.yml` is in the default branch root and README documents usage (done).
2. Create and push a semantic version tag:

```bash
git tag -a v1.0.0 -m "Release GitHub Action v1.0.0"
git push origin v1.0.0
git tag -f v1 v1.0.0
git push -f origin v1
```

3. In GitHub, open your repository page:
   - `Releases` -> `Draft a new release` -> choose `v1.0.0` -> publish.
4. Open the `Marketplace` (GitHub Store) publishing flow from the repository and submit listing metadata.
5. Keep the major tag (`v1`) updated to the latest compatible release.

## Notes

- Notes are written with `git notes add -f -m "<markdown>" <commit-hash>`.
- Conversation markdown labels user messages with your git alias (`git config user.name`) and assistant messages with provider name.
- Serilog debug logs are enabled in `DEBUG` builds.
