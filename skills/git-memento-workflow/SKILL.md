---
name: git-memento-workflow
description: Use git-memento to initialize repository-level AI provider settings, create commits that attach AI session transcripts as git notes, and sync notes to remote. Trigger this skill when users ask to create or troubleshoot memento commits, configure providers (Codex or Claude), verify note attachment, or share notes with team remotes.
---

# Git Memento Workflow

Run this workflow whenever work must be committed with an AI session note.

## 1. Preflight

- Confirm repository status with `git status --short` and branch with `git branch --show-current`.
- Confirm `git memento` is available: `git memento help`.
- If unavailable, install `git-memento` into a directory on `PATH` and re-check.

## 2. Initialize Once Per Repository

- Check configuration: `git config --local --get memento.provider`.
- If missing, run `git memento init codex` or `git memento init claude`.
- If provider CLI shape differs from expected `sessions get/list --json`, configure adapter command in local git config:
  - `git config --local memento.<provider>.bin <adapter-or-cli-path>`
  - `git config --local memento.<provider>.getArgs '<args with {id} placeholder>'`
  - `git config --local memento.<provider>.listArgs '<list args>'`

## 3. Resolve Session ID

- Prefer explicit session id from the user.
- For Codex in this environment, default to `CODEX_THREAD_ID` when present.
- Validate retrieval before commit by running provider command manually if needed.

## 4. Create Memento Commit

- Stage files as needed (`git add ...`).
- Run commit through memento so note attachment happens in the same flow:
  - `git memento commit <session-id> -m "<subject>"`
- For longer body, use embedded newlines in `-m` or omit `-m` to use editor.

## 5. Verify Note Attachment

- Read latest commit: `git log -1 --pretty=fuller`.
- Show note on that commit: `git notes show <commit-hash>`.
- If note is missing, inspect provider command config and rerun commit flow after fixing.

## 6. Push Code and Sync Notes

- Push commit branch: `git push <remote> <branch>`.
- Sync notes with memento: `git memento share-notes <remote>`.
- Verify notes exist remotely: `git ls-remote <remote> 'refs/notes/*'`.

## 7. Failure Handling Checklist

- `git-memento is not configured`: run `git memento init ...`.
- Provider command fails: verify `memento.<provider>.*` git config and executable permissions.
- Session not found: list sessions using provider list command and retry with exact id.
- Note push missing on remote: rerun `git memento share-notes <remote>` and confirm remote ref output.
