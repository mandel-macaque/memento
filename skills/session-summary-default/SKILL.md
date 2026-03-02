---
name: session-summary-default
description: Summarize AI coding sessions into concise, privacy-safe markdown focused on implementation-relevant context.
---

# Session Summary Default

Use this skill to summarize an AI coding session as readable markdown for git notes.

## Requirements

- Output valid markdown only.
- Remove personal data and identifying details.
- Exclude dead-end attempts or paths that produced no useful outcome.
- Keep only context useful for understanding code and decisions.
- Write clear prose that reads like a short specification, not raw logs.

## Output Structure

Use this structure:

1. `## Goal`
2. `## Implemented Changes`
3. `## Key Decisions`
4. `## Validation`
5. `## Follow-ups`

If a section has no relevant content, omit it.

## Redaction Rules

- Do not include names, emails, usernames, tokens, machine paths, or account identifiers unless technically required for understanding behavior.
- Replace sensitive details with neutral placeholders like `[redacted]`.

## Quality Bar

- Keep it concise and precise.
- Prefer concrete outcomes over narration.
- Preserve technical intent and rationale.
