# Team Adoption Guide

This guide explains how to adopt git-memento across your development team. Use this when you want to enable AI session auditing and note sharing for multiple team members working on the same repository.

## Overview

When working with AI coding assistants, individual developers can use git-memento to record their sessions. However, the real value comes when teams share these notes, creating a collective audit trail that supports code review, compliance, and knowledge transfer.

This guide walks you through setting up git-memento for team collaboration, from the initial repository configuration through ongoing synchronization workflows.

## Before you begin

Before starting the team adoption process, ensure:

**Team lead prerequisites:**
- Git repository access with push permissions
- git-memento installed locally (see [Installation](../README.md#installation))
- An AI provider CLI installed (Codex or Claude Code)

**Team member prerequisites:**
- Git repository access (at minimum, clone and fetch permissions)
- git-memento installed locally
- An AI provider CLI installed (same provider as the team lead, or mixed if your team uses multiple providers)

## Initialize the repository

The team lead should initialize git-memento once for the entire repository.

1. Clone or navigate to your repository:
   ```bash
   cd your-repository
   ```

2. Initialize git-memento with your AI provider:
   ```bash
   git memento init codex
   # or
   git memento init claude
   ```

   This creates local configuration in `.git/config`. You do not need to commit any files.

3. Create a test commit with a session note:
   ```bash
   # Make a small change
   echo "# AI Session Audit" >> .memento-test.md
   git add .memento-test.md

   # Create a commit with your session note
   git memento commit <your-session-id> -m "Test: Initialize git-memento for team"
   ```

   Replace `<your-session-id>` with a real session ID from your AI provider.

4. Verify the note was attached:
   ```bash
   git notes show HEAD
   ```

   You should see a markdown-formatted AI conversation transcript.

## Share notes with the team

After creating your first memento commit, configure the remote to share notes.

1. Push your commit and sync notes to the remote:
   ```bash
   git memento push origin
   ```

   This command:
   - Pushes your commits to `origin`
   - Syncs `refs/notes/commits` and `refs/notes/memento-full-audit` to the remote
   - Configures the remote fetch mapping so teammates can retrieve notes

2. Verify notes are on the remote:
   ```bash
   git ls-remote origin 'refs/notes/*'
   ```

   You should see output like:
   ```
   abc123... refs/notes/commits
   def456... refs/notes/memento-full-audit
   ```

## Onboard team members

Each team member needs to install git-memento and configure their local repository to fetch notes.

### Installation

Team members should install git-memento:

```bash
curl -fsSL https://raw.githubusercontent.com/mandel-macaque/memento/main/install.sh | sh
```

Verify installation:

```bash
git memento --version
```

### Clone and configure

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd <repository-name>
   ```

2. Initialize git-memento locally:
   ```bash
   git memento init codex
   # or
   git memento init claude
   ```

   Each team member runs `init` to configure their local git settings. This does not affect other team members' configurations.

3. Fetch notes from the remote:
   ```bash
   git memento notes-sync
   ```

   This command:
   - Configures the local fetch mapping for notes
   - Fetches `refs/notes/*` from the remote
   - Creates backup refs before merging

4. Verify you can see notes from other team members:
   ```bash
   git log --show-notes
   ```

   You should see AI session transcripts attached to commits created with git-memento.

## Create commits with notes

Once configured, team members create commits the same way:

1. Make your changes and stage files:
   ```bash
   git add .
   ```

2. Create a commit with your AI session note:
   ```bash
   git memento commit <session-id> -m "Your commit message"
   ```

3. Verify the note was attached:
   ```bash
   git notes show HEAD
   ```

4. Push your commit and sync notes:
   ```bash
   git memento push
   ```

## Synchronization workflow

### Regular push and pull

When working with shared notes, follow this workflow:

**Before starting work:**
```bash
# Pull latest changes
git pull origin main

# Sync notes from teammates
git memento notes-sync
```

**After completing work:**
```bash
# Create commit with note
git memento commit <session-id> -m "Your changes"

# Push commits and sync notes
git memento push
```

### Handling note conflicts

git-memento uses the `cat_sort_uniq` merge strategy by default, which:
- Concatenates notes from both local and remote
- Sorts lines to create a stable order
- Removes duplicate lines

This strategy rarely produces conflicts. If you need a different strategy, specify it when syncing:

```bash
git memento notes-sync --strategy union
```

Available strategies: `manual`, `ours`, `theirs`, `union`, `cat_sort_uniq`.

### Backup refs for safety

Each time you run `notes-sync`, git-memento creates backup refs under `refs/notes/memento-backups/<timestamp>/...`

If something goes wrong during a merge, you can restore from a backup:

```bash
# List backups
git for-each-ref refs/notes/memento-backups

# Restore from a backup timestamp
git notes --ref refs/notes/commits add -f -m "$(git notes --ref refs/notes/memento-backups/20260302120000/commits show <commit>)" <commit>
```

## Best practices

### When to use summary mode

For sensitive projects or large teams, consider using summary mode instead of full transcripts:

```bash
git memento commit <session-id> --summary-skill default -m "Your changes"
```

Summary mode:
- Stores a condensed summary in `refs/notes/commits`
- Keeps the full transcript in `refs/notes/memento-full-audit`
- Reduces note size while maintaining audit capability
- Requires confirmation before attaching the summary

Use summary mode when:
- Your AI sessions contain sensitive information
- You want to reduce repository size
- Your team needs high-level context rather than full conversations

### Commit message conventions

Establish team conventions for commit messages that reference AI assistance:

**Option 1: Explicit AI indicator**
```bash
git memento commit <session-id> -m "AI: Add user authentication"
```

**Option 2: Regular messages (AI context in note)**
```bash
git memento commit <session-id> -m "Add user authentication"
```

The note attachment provides the AI session context, so the commit message can follow your existing conventions.

### Review workflow with notes

During code review, reviewers can access AI session notes to understand the reasoning behind changes:

**On GitHub:**
- If you use the [git-memento GitHub Action](../README.md#cicd-integration) in `comment` mode, notes appear automatically as commit comments
- Reviewers can read the AI conversation directly in the PR

**On the command line:**
```bash
# Review a specific commit's note
git notes show <commit-sha>

# View notes for a range of commits
git log --show-notes origin/main..feature-branch

# Search notes for specific keywords
git log --grep-reflog="Provider: Claude" --walk-reflogs
```

### Handling mixed AI providers

Teams can use multiple AI providers (Codex and Claude Code) simultaneously:

1. Each developer runs `git memento init` with their preferred provider
2. git-memento marks each note with the provider name (`- Provider: Codex` or `- Provider: Claude`)
3. Notes from different providers coexist in the same repository
4. The `amend` command can append sessions from different providers to the same commit

## Troubleshooting

### Notes not appearing after clone

**Symptom:** A team member clones the repository but doesn't see any notes.

**Solution:**
```bash
git memento notes-sync
```

The first sync configures the fetch mapping and retrieves notes from the remote.

### Notes missing after push

**Symptom:** You pushed commits but teammates don't see your notes.

**Solution:**
Ensure you used `git memento push` or `git memento share-notes`, not plain `git push`. Regular `git push` does not sync notes by default.

```bash
# Sync notes after a plain push
git memento share-notes
```

### Session ID not found

**Symptom:** `git memento commit` fails with "session not found."

**Solution:**
List available sessions from your AI provider:

```bash
# For Codex
codex sessions list --json

# For Claude Code
claude sessions list --json
```

Copy the exact session ID from the output and retry.

### Provider not configured

**Symptom:** Command fails with "git-memento is not configured for this repository."

**Solution:**
```bash
git memento init codex
# or
git memento init claude
```

Each team member must run `init` in their local repository.

## See also

- [Getting Started](../README.md#getting-started) - Individual setup instructions
- [Core Commands](../README.md#core-commands) - Full command reference
- [CI/CD Integration](../README.md#cicd-integration) - Automate note comments and enforcement
- [Advanced Features](../README.md#advanced-features) - Note carry-over, rebase handling, and custom provider configuration
