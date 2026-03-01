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
git memento amend -m "Amended subject"
git memento amend <new-session-id> -m "Amended subject" -m "Amended body"
git memento audit --range main..HEAD --strict
git memento doctor
```

Or:

```bash
git memento commit <session-id>
```

You can pass `-m` multiple times, and each value is forwarded to `git commit` in order.
When `-m` is omitted, `git commit` opens your default editor.

`amend` runs `git commit --amend`.
- Without a session id, it copies the note(s) from the previous HEAD onto the amended commit.
- With a session id, it copies previous note(s) and appends the new fetched session as an additional session entry.
- A single commit note can contain sessions from different AI providers.

Share notes with the repository remote (default: `origin`):

```bash
git memento share-notes
git memento share-notes upstream
```

This pushes `refs/notes/*` and configures local `remote.<name>.fetch` so notes can be fetched by teammates.

Push your branch and sync notes to the same remote in one command (default: `origin`):

```bash
git memento push
git memento push upstream
```

This runs `git push <remote>` and then performs the same notes sync as `share-notes`.

Sync and merge notes from a remote safely (default remote: `origin`, default strategy: `cat_sort_uniq`):

```bash
git memento notes-sync
git memento notes-sync upstream
git memento notes-sync upstream --strategy union
```

This command:
- Ensures notes fetch mapping is configured.
- Creates a backup ref under `refs/notes/memento-backups/<timestamp>`.
- Fetches remote notes into `refs/notes/remote/<remote>/*`.
- Merges remote notes into local notes and pushes synced notes back to the remote.

Configure automatic note carry-over for rewritten commits (`rebase` / `commit --amend`):

```bash
git memento notes-rewrite-setup
```

This sets local git config:
- `notes.rewriteRef=refs/notes/commits`
- `notes.rewriteMode=concatenate`
- `notes.rewrite.rebase=true`
- `notes.rewrite.amend=true`

Carry notes from a rewritten range (for squash/rewrite flows) onto a new target commit:

```bash
git memento notes-carry --onto <new-commit> --from-range <base>..<head>
```

This reads notes from commits in `<base>..<head>` and appends a provenance block to `<new-commit>`.

Audit note coverage and note metadata in a commit range:

```bash
git memento audit --range main..HEAD
git memento audit --range origin/main..HEAD --strict --format json
```

- Reports commits with missing notes (`missing-note <sha>`).
- Validates note metadata markers (`- Provider:` and `- Session ID:`).
- In `--strict` mode, invalid note structure fails the command.

Run repository diagnostics for provider config, notes refs, and remote sync posture:

```bash
git memento doctor
git memento doctor upstream --format json
```

Show command help:

```bash
git memento help
```

Show installed tool version (major.minor + commit metadata when available):

```bash
git memento --version
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
- If the repository is not configured yet, `commit`, `amend <session-id>`, `push`, `share-notes`, `notes-sync`, `notes-rewrite-setup`, and `notes-carry` fail with a message to run `git memento init` first.
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

## Release Automation

- Release assets are built with NativeAOT (`PublishAot=true`) and packaged as a single executable per platform.
- If the workflow runs from a tag push (for example `v1.2.3`), that tag is used as the GitHub release tag/name.
- If the workflow runs from `main` without a tag, the release tag becomes `<Version>-<shortSha>` (for example `1.0.0-a1b2c3d4`).
- `install.sh` always downloads from `releases/latest`, so the installer follows the latest published GitHub release.

## Install Script CI Coverage

CI runs install smoke tests on Linux, macOS, and Windows that verify:

- `install.sh` downloads the latest release asset for the current OS/architecture.
- The binary is installed for the current user into the configured install directory.
- `git memento --version` and `git memento help` both execute after installation.

## Test

```bash
dotnet test GitMemento.slnx
npm run test:js
```

## Commit Note Comments + CI Gate (GitHub Action)

This repository includes a reusable marketplace action with two modes:

- `mode: comment` (default): reads `git notes` created by `git-memento` and posts a commit comment.
- `mode: gate`: runs `git memento audit` as a CI gate and fails if note coverage checks fail.

Action definition:

- `action.yml` at repository root.
- Renderer source: `src/note-comment-renderer.ts`
- Runtime artifact committed for marketplace consumers: `dist/note-comment-renderer.js`

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
          mode: comment
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

Inputs:

- `github-token` (default: `${{ github.token }}`)
- `mode` (default: `comment`) - `comment` or `gate`
- `notes-fetch-refspec` (default: `refs/notes/*:refs/notes/*`)
- `max-comment-length` (default: `65000`)
- `audit-range` (optional, gate mode)
- `base-ref` (optional, gate mode pull request inference)
- `strict` (default: `true`, gate mode)
- `memento-repo` (default: `mandel-macaque/memento`, gate mode installer source)

CI gate example:

```yaml
name: memento-note-gate

on:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: read

jobs:
  enforce-memento-notes:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: mandel-macaque/memento@v1
        with:
          mode: gate
          strict: "true"
```

Local workflow in this repository:

- `.github/workflows/memento-note-comments.yml`

### Publish This Action To GitHub Marketplace

1. Build and commit the action renderer artifact:

```bash
npm ci
npm run build:action
git add src/note-comment-renderer.ts dist/note-comment-renderer.js
```

2. Ensure `action.yml` is in the default branch root and README documents usage.
3. Create and push a semantic version tag:

```bash
git tag -a v1.0.0 -m "Release GitHub Action v1.0.0"
git push origin v1.0.0
git tag -f v1 v1.0.0
git push -f origin v1
```

4. In GitHub, open your repository page:
   - `Releases` -> `Draft a new release` -> choose `v1.0.0` -> publish.
5. Open the `Marketplace` (GitHub Store) publishing flow from the repository and submit listing metadata.
6. Keep the major tag (`v1`) updated to the latest compatible release.

## Notes

- Notes are written with `git notes add -f -m "<markdown>" <commit-hash>`.
- Multi-session notes use explicit delimiters:
  - `<!-- git-memento-sessions:v1 -->`
  - `<!-- git-memento-note-version:1 -->`
  - `<!-- git-memento-session:start -->`
  - `<!-- git-memento-session:end -->`
- Legacy single-session notes remain supported and are upgraded to the versioned multi-session envelope when amend needs to append a new session.
- Conversation markdown labels user messages with your git alias (`git config user.name`) and assistant messages with provider name.
- Serilog debug logs are enabled in `DEBUG` builds.
