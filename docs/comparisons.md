# AI Session Recording in Git: Landscape and Comparisons

As AI coding assistants become integral to development workflows, a new category
of tooling has emerged: **preserving AI session context in version control**.
This document surveys the landscape as of early 2026 and positions git-memento
relative to other approaches.

## The Problem

When an AI assistant produces a commit, the conversation that led to that change
is typically lost. Team members see *what* changed but not *why* the AI was
asked to change it, what alternatives were considered, or what constraints were
given. This matters for:

- **Code review** - understanding intent behind AI-generated changes.
- **Onboarding** - learning how a codebase evolved through AI sessions.
- **Compliance** - audit trails required by SOC 2, ISO 27001, and the EU AI Act
  (high-risk enforcement begins August 2026).
- **Debugging** - recovering the reasoning when AI-generated code breaks.

## Design Space

Tools in this space differ along two main axes:

1. **Storage mechanism** - where session data lives in (or alongside) git.
2. **Capture granularity** - from line-level attribution to full transcripts.

```
                    MORE DATA CAPTURED
                          |
       Entire CLI *       |       * Git AI (line-level)
       (full sessions,    |         (authorship attribution,
        shadow branches)  |          git notes + .git/ai/)
                          |
  SEPARATE ---------------+---------------- GIT-NATIVE
  STORAGE                 |                 STORAGE
                          |
       Gryph *            |       * git-memento
       (SQLite audit)     |         (git notes, markdown)
                          |
                    LESS DATA CAPTURED
```

## Dedicated Recording Tools

### git-memento

**Approach**: Wraps `git commit` to fetch the AI session transcript from the
provider CLI, render it as clean markdown, and store it as a **git note** on
the resulting commit.

- **Storage**: Native `git notes` (`refs/notes/commits`). Notes are invisible
  to normal `git log` unless explicitly requested. They do not appear in diffs,
  do not affect file trees, and can be selectively synced between remotes.
- **Granularity**: Conversation-level (speaker roles, messages, metadata).
- **Providers**: Codex, Claude Code (extensible via configuration).
- **CI integration**: GitHub Action with `comment` mode (post notes as commit
  comments) and `gate` mode (enforce note coverage on PRs).
- **Trade-offs**: Minimal footprint; notes add no weight to clones unless
  fetched. Does not capture file-level diffs or support session rewind.

### Entire CLI

[github.com/entireio/cli](https://github.com/entireio/cli) - Go, backed by
$60M seed funding.

**Approach**: Hooks into AI agent lifecycle events (prompt submit, tool
completion, session start/stop) to capture full session data automatically.

- **Storage**: Dedicated orphan branch (`entire/checkpoints/v1`) for permanent
  metadata. Temporary "shadow branches" (`entire/<hash>-<worktreeHash>`) during
  active sessions for checkpoint/rewind support. Commit trailers
  (`Entire-Checkpoint: <id>`) link user commits to metadata.
- **Granularity**: Full JSONL transcripts, prompts, tool calls, token usage,
  file change snapshots.
- **Agents**: Claude Code, Gemini CLI, OpenCode, Cursor.
- **Key features**: Interactive session rewind to any checkpoint; resume
  interrupted sessions; concurrent worktree support; nested sub-agent tracking.
- **Trade-offs**: Richest data capture; requires hook installation; metadata
  branch grows over time; tighter coupling to supported agents.

### Git AI

[github.com/git-ai-project/git-ai](https://github.com/git-ai-project/git-ai) -
Rust, ~1,200 GitHub stars.

**Approach**: Tracks **line-level authorship** via pre/post-edit hooks.
Checkpoints (small diffs) accumulate in `.git/ai/` during a session, then get
processed into an authorship log stored as a git note on commit.

- **Storage**: Git notes (`refs/notes/ai`) for authorship logs; local SQLite or
  cloud store for full transcripts.
- **Granularity**: Per-line attribution (which lines are AI-written vs human).
- **Agents**: 10+ (Claude Code, Copilot, Cursor, Continue, Gemini, Codex,
  OpenCode, Droid, Junie, Rovo Dev).
- **Key features**: `git-ai blame` (AI-aware replacement for `git blame`); IDE
  decorations in VS Code, Cursor, Windsurf, Emacs; attribution survives
  rebases, squashes, cherry-picks; enterprise dashboards.
- **Trade-offs**: Most granular attribution; more complex setup; authorship
  tracking does not preserve conversation context by default.

## Specifications

### Agent Trace

[github.com/cursor/agent-trace](https://github.com/cursor/agent-trace) -
RFC v0.1.0 (January 2026).

An **open specification** (not a tool) for recording AI code attribution.
Backed by Cursor, Cognition (Devin), Cloudflare, Vercel, and Google Jules.
Defines a vendor-neutral JSON schema for tracking which code came from AI,
linking to conversation URLs, model versions, and line ranges.
Storage-agnostic - trace records can live as files, git notes, database entries,
or other mechanisms.

## Audit and Observability Tools

These tools record AI agent activity but do not store data inside git:

| Tool | Storage | Focus | Agents |
|------|---------|-------|--------|
| [Gryph](https://github.com/safedep/gryph) (Go) | Local SQLite | All agent actions, queryable audit trail | Claude Code, Cursor, Gemini CLI, OpenCode, Windsurf |
| [Vigilo](https://github.com/Idan3011/vigilo) (Rust) | Local JSONL (AES-256-GCM encrypted) | Tool calls, cost tracking | Claude Code, Cursor |

## Session Browsers and Exporters

Read-only tools for searching and reviewing existing session data:

- [Agent Sessions](https://github.com/jazzyalex/agent-sessions) - native macOS
  app for browsing sessions across Codex, Claude Code, Gemini CLI, Copilot CLI,
  and others.
- [claude-conversation-extractor](https://github.com/ZeroSumQuant/claude-conversation-extractor) -
  Python tool to export Claude Code conversations as markdown.
- [claude-history](https://github.com/raine/claude-history) - fuzzy-search
  Claude Code conversation history.

## Built-In Agent Capabilities

Major AI coding tools provide varying levels of session persistence, but none
natively link session transcripts to git commits:

| Agent | Session storage | Git integration | Export |
|-------|----------------|-----------------|--------|
| **Claude Code** | JSONL in `~/.claude/projects/` | `Co-Authored-By` trailers | `/export` command |
| **Codex CLI** | JSONL in `~/.codex/sessions/` | None built-in | `sessions get <id> --json` |
| **Gemini CLI** | Automatic session recording | None built-in | `/export jsonl`, `/export markdown` |
| **Aider** | `.aider.chat.history.md` | Auto-commits every change | Markdown log files |
| **Cursor** | Enterprise observability logs | Hooks system (v1.7+) | Via hooks |

This gap - session data exists but is not linked to commits - is what
git-memento and the other dedicated tools above fill.

## Comparison Summary

| | git-memento | Entire CLI | Git AI |
|---|---|---|---|
| **Storage** | Git notes | Orphan branch + shadow branches | Git notes + `.git/ai/` |
| **Data** | Markdown conversation | Full JSONL transcripts + snapshots | Line-level authorship logs |
| **Invasiveness** | Minimal (notes invisible by default) | Moderate (branches + trailers) | Moderate (notes + local DB) |
| **Rewind/resume** | No | Yes | No |
| **CI gate** | Yes (GitHub Action) | Via pre-push hook | Enterprise dashboard |
| **Agent count** | 2 | 4 | 10+ |
| **Language** | F# (NativeAOT) | Go | Rust |

### When to choose what

- **git-memento** when you want the lightest-touch approach: native git
  primitives, no extra branches, invisible unless you look. Good for teams
  adopting session recording incrementally.
- **Entire CLI** when you want full observability with rewind and checkpoint
  support. Best for workflows where recovering from AI missteps is critical.
- **Git AI** when line-level "who wrote this line" attribution matters more
  than preserving the conversation itself. Best for compliance-heavy
  environments and large teams tracking AI adoption metrics.

These tools are largely complementary. A team could use git-memento for
lightweight conversation capture alongside Git AI for authorship attribution.

## Further Reading

- [Fingerprinting AI Coding Agents on GitHub](https://arxiv.org/abs/2601.17406)
  (MSR 2026) - behavioral fingerprints left by AI agents in PRs.
- [Agentic Much? Adoption of Coding Agents on GitHub](https://arxiv.org/html/2601.18341v1) -
  15-23% adoption rate across 129K+ GitHub projects.
- [The AI Code Tracking Revolution](http://blog.brightcoding.dev/2025/12/14/the-ai-code-tracking-revolution-how-to-automatically-identify-ai-generated-code-in-your-git-repositories) -
  overview of AI code identification approaches.
- [Agent Trace announcement](https://cognition.ai/blog/agent-trace) -
  Cognition's perspective on the shift from "lines of code" to "context".
