# git-memento

`git-memento` is a Git extension that records the AI coding session used to produce a commit. It attaches AI conversation transcripts as git notes, creating an audit trail for AI-assisted development.

## Table of Contents

- [What is git-memento?](#what-is-git-memento)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
  - [First-time setup](#first-time-setup)
  - [Your first memento commit](#your-first-memento-commit)
  - [Verify the note](#verify-the-note)
  - [Share with your team](#share-with-your-team)
- [Core Commands](#core-commands)
- [Advanced Features](#advanced-features)
- [CI/CD Integration](#cicd-integration)
- [Build and Install](#build-and-install)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)

## What is git-memento?


git-memento solves a critical problem in AI-assisted development: when an AI assistant produces a commit, the conversation that led to that change is typically lost. Team members see *what* changed but not *why* the AI was asked to change it, what alternatives were considered, or what constraints were given.

git-memento:
- Creates commits with normal Git flow (`-m` or editor)
- Attaches the AI session transcript to the commit using `git notes`
- Produces human-readable markdown notes
- Keeps provider support extensible (Codex and Claude Code supported)
- Works seamlessly with your existing Git workflow

**Who is this for?** Development teams using AI coding assistants (Codex, Claude Code) who want to maintain transparency, support code review, meet compliance requirements, or preserve context for debugging and onboarding.

## Getting Started

### Prerequisites

Before using git-memento, you need:

- Git installed and configured (`git config user.name` and `user.email` set)
- An AI provider CLI installed:
  - **Codex**: Install via `npm install -g @codexcli/cli` or your provider's instructions
  - **Claude Code**: Install from [claude.ai/code](https://claude.ai/code)

### Installation

Install git-memento from the latest GitHub release:

```bash
curl -fsSL https://raw.githubusercontent.com/mandel-macaque/memento/main/install.sh | sh
```

The installer will:
1. Detect your OS and architecture
2. Download the appropriate release binary
3. Install to `~/.local/bin` (or your configured install directory)
4. Prompt you to add the directory to your `PATH` if needed

**Alternative**: You can also [build from source](#build-aot) if you prefer.

After installation, verify git-memento is available:

```bash
git memento --version
git memento help
```

### First-time setup

Initialize git-memento for your repository. This stores provider configuration in local git metadata (`.git/config`):

```bash
# For Codex
git memento init codex

# For Claude Code
git memento init claude
```

You only need to run `init` once per repository. The configuration is stored locally and won't affect other repositories.

### Your first memento commit

1. **Make your changes** as you normally would
2. **Stage your files**:
   ```bash
   git add .
   ```

3. **Create a commit with an attached AI session note**:
   ```bash
   git memento commit <session-id> -m "Your commit message"
   ```

   - Replace `<session-id>` with your AI session ID
   - For **Codex**: Find session IDs with `codex sessions list --json`
   - For **Claude Code**: Use the session ID from your Claude session

   Example:
   ```bash
   git memento commit abc123 -m "Add user authentication feature"
   ```

### Verify the note

Check that the AI session transcript was attached to your commit:

```bash
# Show the commit
git log -1 --pretty=fuller

# Show the attached note
git notes show HEAD
```

You should see a markdown-formatted conversation with your AI provider showing the messages exchanged during the session.

### Share with your team

Push your commit and sync the notes to your remote repository:

```bash
git memento push
```

This command:
- Pushes your commits to the remote
- Syncs `refs/notes/*` to the remote
- Configures fetch mappings so teammates can retrieve notes

Your team members can fetch the notes with:

```bash
git memento notes-sync
```

**You're now up and running!** Continue reading for more advanced features and commands.

## Core Commands

### Initialize per-repository

```bash
git memento init
git memento init codex
git memento init claude
```

`init` stores configuration in local git metadata (`.git/config`) under `memento.*`.

### Create commits with notes

```bash
git memento commit <session-id> -m "Normal commit message"
git memento commit <session-id> -m "Subject line" -m "Body paragraph"
git memento commit <session-id> --summary-skill default -m "Subject line"
```

You can pass `-m` multiple times, and each value is forwarded to `git commit` in order. When `-m` is omitted, `git commit` opens your default editor.

### Amend commits

```bash
git memento amend -m "Amended subject"
git memento amend <new-session-id> -m "Amended subject" -m "Amended body"
git memento amend <new-session-id> --summary-skill session-summary-default -m "Amended subject"
```

`amend` runs `git commit --amend`:
- Without a session id, it copies the note(s) from the previous HEAD onto the amended commit
- With a session id, it copies previous note(s) and appends the new fetched session as an additional session entry
- A single commit note can contain sessions from different AI providers

### Summary mode

`--summary-skill <skill|default>` (for `commit` and `amend <session-id>`) changes note behavior:
- The default notes ref (`refs/notes/commits`) stores a summary record instead of the full transcript
- The full session is stored in `refs/notes/memento-full-audit`
- The CLI prints the generated summary and asks for confirmation
- If rejected, you must provide a prompt to regenerate
- `default` maps to the repository skill at `skills/session-summary-default/SKILL.md`
- The default summary skill is always applied as a baseline; if a user-provided summary skill conflicts with it, user-provided instructions take precedence

Verify both notes after a summary run:

```bash
git notes show <commit-hash>
git notes --ref refs/notes/memento-full-audit show <commit-hash>
```

### Share notes with remote

```bash
git memento share-notes
git memento share-notes upstream
```

This pushes `refs/notes/*` and configures local `remote.<name>.fetch` so notes can be fetched by teammates.

### Push branch and sync notes

```bash
git memento push
git memento push upstream
```

This runs `git push <remote>` and then performs the same notes sync as `share-notes`. Default remote is `origin`.

### Sync notes from remote

If a session id is not found, `git-memento` asks the configured provider for available sessions and prints them.

This command:
- Ensures notes fetch mapping is configured
- Creates backup refs under `refs/notes/memento-backups/<timestamp>/...`
- Fetches remote notes into `refs/notes/remote/<remote>/*`
- Merges remote notes into local notes and pushes synced notes back to the remote
- Syncs both `refs/notes/commits` and `refs/notes/memento-full-audit`

```bash
git memento notes-sync
git memento notes-sync upstream
git memento notes-sync upstream --strategy union
```

Default remote is `origin`, default strategy is `cat_sort_uniq`.

### Audit note coverage

```bash
git memento audit --range main..HEAD
git memento audit --range origin/main..HEAD --strict --format json
```

Audit note coverage and note metadata in a commit range:
- Reports commits with missing notes (`missing-note <sha>`)
- Validates note metadata markers (`- Provider:` and `- Session ID:`)
- In `--strict` mode, invalid note structure fails the command

### Repository diagnostics

```bash
git memento doctor
git memento doctor upstream --format json
```

Run repository diagnostics for provider config, notes refs, and remote sync posture.

### Show help and version

```bash
git memento help
git memento --version
```

## Advanced Features

### Configure automatic note carry-over

For rewritten commits (`rebase` / `commit --amend`):

```bash
git memento notes-rewrite-setup
```

This sets local git config:
- `notes.rewriteRef=refs/notes/*`
- `notes.rewriteMode=concatenate`
- `notes.rewrite.rebase=true`
- `notes.rewrite.amend=true`

### Carry notes from rewritten range

For squash/rewrite flows onto a new target commit:

```bash
git memento notes-carry --onto <new-commit> --from-range <base>..<head>
```

This reads notes from commits in `<base>..<head>` and appends provenance blocks to `<new-commit>`. It carries both `refs/notes/commits` and `refs/notes/memento-full-audit`.

### Provider configuration

Provider defaults can come from env vars, and `init` persists the selected provider + values in local git config:

- `MEMENTO_AI_PROVIDER` (default: `codex`)
- `MEMENTO_CODEX_BIN` (default: `codex`)
- `MEMENTO_CODEX_GET_ARGS` (default: `sessions get {id} --json`)
- `MEMENTO_CODEX_LIST_ARGS` (default: `sessions list --json`)
- `MEMENTO_CODEX_SUMMARY_BIN` (default: `codex`)
- `MEMENTO_CODEX_SUMMARY_ARGS` (default: `exec -c skill.effective_path={effectiveSkillPath} -c skill.default_path={defaultSkillPath} -c skill.user_path={userSkillPath} "{prompt}"`)
- `MEMENTO_CLAUDE_BIN` (default: `claude`)
- `MEMENTO_CLAUDE_GET_ARGS` (default: `sessions get {id} --json`)
- `MEMENTO_CLAUDE_LIST_ARGS` (default: `sessions list --json`)
- `MEMENTO_CLAUDE_SUMMARY_BIN` (default: `claude`)
- `MEMENTO_CLAUDE_SUMMARY_ARGS` (default: `-p --append-system-prompt "Skill paths: effective={effectiveSkillPath}; default={defaultSkillPath}; user={userSkillPath}. Prefer user skill when provided." "{prompt}"`)

Summary args template placeholders:
- `{prompt}`: generated summary prompt
- `{sessionId}`: session id
- `{skill}`: user skill name or `session-summary-default`
- `{defaultSkillPath}`: default skill file path (`skills/session-summary-default/SKILL.md`)
- `{userSkillPath}`: user skill file path (empty when `--summary-skill default`)
- `{effectiveSkillPath}`: user skill path when provided, otherwise default skill path

Security behavior:
- Session transcript is treated as untrusted data
- Summary prompt explicitly instructs the model not to follow instructions embedded in transcript content

Set `MEMENTO_AI_PROVIDER=claude` to use Claude Code.

### Runtime behavior

- If the repository is not configured yet, `commit`, `amend <session-id>`, `push`, `share-notes`, `notes-sync`, `notes-rewrite-setup`, and `notes-carry` fail with a message to run `git memento init` first
- Stored git metadata keys include:
  - `memento.provider`
  - `memento.codex.bin`, `memento.codex.getArgs`, `memento.codex.listArgs`, `memento.codex.summary.bin`, `memento.codex.summary.args`
  - `memento.claude.bin`, `memento.claude.getArgs`, `memento.claude.listArgs`, `memento.claude.summary.bin`, `memento.claude.summary.args`

If a session id is not found, `git-memento` asks the provider for available sessions and prints them.

### Notes format

- Notes are written with `git notes add -f -m "<markdown>" <commit-hash>`
- Multi-session notes use explicit delimiters:
  - `<!-- git-memento-sessions:v1 -->`
  - `<!-- git-memento-note-version:1 -->`
  - `<!-- git-memento-session:start -->`
  - `<!-- git-memento-session:end -->`
- Legacy single-session notes remain supported and are upgraded to the versioned multi-session envelope when amend needs to append a new session
- Conversation markdown labels user messages with your git alias (`git config user.name`) and assistant messages with provider name
- Serilog debug logs are enabled in `DEBUG` builds

## CI/CD Integration

This repository includes a reusable marketplace action with three modes:

- `mode: comment` (default): reads `git notes` created by `git-memento` and posts a commit comment.
- `mode: gate`: runs `git memento audit` as a CI gate and fails if note coverage checks fail. `git-memento` must already be installed in the job.
- `mode: merge-carry`: on merged pull requests, carries notes from PR commits onto the merge commit and pushes `refs/notes/*`.

Action definition:

- `action.yml` at repository root
- `install/action.yml` for reusable git-memento installation
- Renderer source: `src/note-comment-renderer.ts`
- Runtime artifact committed for marketplace consumers: `dist/note-comment-renderer.js`

### Example: Post commit comments

```yaml
name: memento-note-comments

on:
  push:
  pull_request:
    types: [opened, synchronize, reopened, labeled, unlabeled]

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
- `mode` (default: `comment`) - `comment`, `gate`, or `merge-carry`
- `notes-fetch-refspec` (default: `refs/notes/*:refs/notes/*`)
- `max-comment-length` (default: `65000`)
- `audit-range` (optional, gate mode)
- `base-ref` (optional, gate mode pull request inference)
- `strict` (default: `true`, gate mode)
- `ignore-label` (default: `ignore-notes`, gate mode)
  - If present on a pull request, gate note checks are skipped and a PR comment with `Notes ignored` is posted.
- `carry-onto` (optional, merge-carry mode) - target commit SHA. Defaults to `pull_request.merge_commit_sha`.
- `carry-range` (optional, merge-carry mode) - explicit `<base>..<head>` source range.
- `carry-base-sha` (optional, merge-carry mode) - base SHA used when `carry-range` is empty.
- `carry-head-sha` (optional, merge-carry mode) - head SHA used when `carry-range` is empty.
- `carry-provider` (default: `codex`, merge-carry mode) - provider value set in local git config for `notes-carry`.

Installer action inputs:

- `install-dir` (default: `${{ runner.temp }}/git-memento-bin`)
- `verify` (default: `true`)

CI gate example:

```yaml
name: memento-note-gate

on:
  pull_request:
    types: [opened, synchronize, reopened, labeled, unlabeled]

permissions:
  contents: read
  issues: write
  pull-requests: write

jobs:
  enforce-memento-notes:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: mandel-macaque/memento/install@v1

      - uses: mandel-macaque/memento@v1
        with:
          mode: gate
          strict: "true"
          ignore-label: "ignore-notes"
```

Merge-carry example:

```yaml
name: memento-notes-merge-carry

on:
  pull_request:
    types: [closed]

permissions:
  contents: write

jobs:
  carry-notes-to-merge-commit:
    if: github.event.pull_request.merged == true
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: mandel-macaque/memento/install@v1
        with:
          memento-repo: mandel-macaque/memento

      - uses: mandel-macaque/memento@v1
        with:
          mode: merge-carry
```

Installer action example:

```yaml
- uses: mandel-macaque/memento/install@v1
```

### Action inputs

- `.github/workflows/memento-note-comments.yml`
- `.github/workflows/memento-note-gate.yml`
- `.github/workflows/memento-notes-merge-carry.yml`
  - Uses the public action with `mode: merge-carry`.
  - Carries notes from PR source commits onto the merge commit.
  - Pushes updated `refs/notes/*` so merged commits keep note visibility in downstream checks.

### Publish this action to GitHub Marketplace

1. Build and commit the action renderer artifact:

```bash
npm ci
npm run build:action
git add src/note-comment-renderer.ts dist/note-comment-renderer.js
```

2. Ensure `action.yml` and `install/action.yml` are in the default branch and README documents usage
3. Create and push a semantic version tag:

```bash
git tag -a v1.0.0 -m "Release GitHub Action v1.0.0"
git push origin v1.0.0
git tag -f v1 v1.0.0
git push -f origin v1
```

4. In GitHub, open your repository page:
   - `Releases` -> `Draft a new release` -> choose `v1.0.0` -> publish
5. Open the `Marketplace` (GitHub Store) publishing flow from the repository and submit listing metadata
6. Keep the major tag (`v1`) updated to the latest compatible release

Local workflow in this repository:

- `.github/workflows/memento-note-comments.yml`
- `.github/workflows/memento-note-gate.yml`
- `.github/workflows/memento-notes-merge-carry.yml`

## Build and Install

### Build (AOT)

Requires `.NET SDK 10` and native toolchain dependencies for NativeAOT.

#### macOS

```bash
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r osx-arm64 -p:PublishAot=true
```

#### Linux

```bash
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r linux-x64 -p:PublishAot=true
```

#### Windows (PowerShell)

```powershell
dotnet publish src/GitMemento.Cli/GitMemento.Cli.fsproj -c Release -r win-x64 -p:PublishAot=true
```

### Local install as Git tool

Git discovers commands named `git-<name>` in `PATH`.

1. Publish for your platform
2. Copy the produced executable to a directory in your `PATH`
3. Ensure the binary name is `git-memento` (or `git-memento.exe` on Windows)

Then run:

```bash
git memento commit <session-id> -m "message"
```

### Release automation

- Release assets are built with NativeAOT (`PublishAot=true`) and packaged as a single executable per platform
- If the workflow runs from a tag push (for example `v1.2.3`), that tag is used as the GitHub release tag/name
- If the workflow runs from `main` without a tag, the release tag becomes `<Version>-<shortSha>` (for example `1.0.0-a1b2c3d4`)
- `install.sh` always downloads from `releases/latest`, so the installer follows the latest published GitHub release

### Install script CI coverage

CI runs install smoke tests on Linux, macOS, and Windows that verify:

- `install.sh` downloads the latest release asset for the current OS/architecture
- The binary is installed for the current user into the configured install directory
- `git memento --version` and `git memento help` both execute after installation

## Testing

```bash
dotnet test GitMemento.slnx
npm run test:js
```

## Contributing

Contributions are welcome! To contribute:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests to ensure everything works
5. Submit a pull request

For development setup, see [Build (AOT)](#build-aot).

## License

See [LICENSE](LICENSE) file for details.