"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { buildBody, buildNoSessionBody, marker } = require("../tools/note-comment-renderer");

const baseHeader = `# Git Memento Session\n\n- Provider: Codex\n- Session ID: sess-123\n- Committer: Mandel\n`;
const countMatches = (value, pattern) => (value.match(pattern) || []).length;

test("renders markdown file sections in collapsible details blocks", () => {
  const flattenedAgents =
    "## Skills A skill is a set of local instructions to follow. ### Available skills - git-memento-workflow: Use git-memento. ### How to use skills - Discovery: open SKILL.md.";

  const note = `${baseHeader}\n### Codex\n\n# AGENTS.md instructions for /Users/mandel/Work/memento\n\n<INSTRUCTIONS>\n${flattenedAgents}\n</INSTRUCTIONS>`;

  const body = buildBody(note, 65000);

  assert.match(body, new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  assert.match(body, /### Markdown files/);
  assert.match(body, /<summary>AGENTS\.md instructions for \/Users\/mandel\/Work\/memento<\/summary>/);
  assert.match(body, /~~~markdown/);
  assert.match(body, /## Skills\nA skill is a set of local instructions to follow\./);
  assert.match(body, /### Available skills/);
  assert.doesNotMatch(body, /Embedded markdown moved below:/);
});

test("leaves regular notes as a single details block", () => {
  const note = `${baseHeader}\n## Conversation\n\n### Mandel\n\nShip the parser.`;
  const body = buildBody(note, 65000);

  assert.match(body, /<summary>The note attached to the commit<\/summary>/);
  assert.doesNotMatch(body, /### Markdown files/);
  assert.match(body, /## Conversation/);
});

test("drops nested markdown file sections before truncating the note", () => {
  const note = `${baseHeader}\n### Codex\n\n# AGENTS.md\n\n<INSTRUCTIONS>\n## Skills A skill is a set of local instructions.\n</INSTRUCTIONS>`;
  const fullBody = buildBody(note, 65000);
  const body = buildBody(note, fullBody.length - 1);

  assert.doesNotMatch(body, /### Markdown files/);
  assert.match(body, /_Nested markdown sections omitted due to GitHub comment size limits\._/);
});

test("keeps full Codex instruction blocks until </INSTRUCTIONS>", () => {
  const note = `${baseHeader}\n### Codex\n\n# AGENTS.md instructions for /Users/mandel/Work/memento\n\n<INSTRUCTIONS>\n## Skills\nA skill is a set of local instructions.\n### Available skills\n- git-memento-workflow\n### How to use skills\n- Discovery\n</INSTRUCTIONS>\n\n### Mandel\n\nShip it.`;
  const body = buildBody(note, 65000);

  assert.match(body, /### Markdown files/);
  assert.match(body, /### Available skills/);
  assert.match(body, /### How to use skills/);
  assert.match(body, /### Mandel\s+Ship it\./);
  assert.doesNotMatch(body, /# AGENTS\.md instructions for \/Users\/mandel\/Work\/memento[\s\S]*# AGENTS\.md instructions for \/Users\/mandel\/Work\/memento/);
});

test("uses provider-aware extraction for Claude notes", () => {
  const claudeHeader = `# Git Memento Session\n\n- Provider: Claude\n- Session ID: sess-claude\n- Committer: Mandel\n`;
  const note = `${claudeHeader}\n### Claude\n\n# PROMPT.md\n\n<INSTRUCTIONS>\n## Context\n### Constraints\n- Keep full markdown.\n</INSTRUCTIONS>\n\n### Mandel\n\nDone.`;
  const body = buildBody(note, 65000);

  assert.match(body, /created with agent Claude \/ sess-claude/);
  assert.match(body, /<summary>PROMPT\.md<\/summary>/);
  assert.match(body, /### Constraints/);
  assert.match(body, /### Mandel\s+Done\./);
});

test("removes session-title instruction blocks from top note body and renders once in markdown files", () => {
  const note = `# Git Memento Session\n\n- Provider: Codex\n- Session ID: sess-meta\n- Session Title: # AGENTS.md instructions for /Users/mandel/Work/memento\n<INSTRUCTIONS>\n## Skills\n### Available skills\n- git-memento-workflow\n</INSTRUCTIONS>\n- Committer: Mandel\n- Captured At (UTC): 2026-03-01T00:35:39.9280860+00:00\n\n## Conversation\n\n### Mandel\n\nFix it.`;
  const body = buildBody(note, 65000);

  assert.match(body, /### Markdown files/);
  assert.match(body, /<summary>AGENTS\.md instructions for \/Users\/mandel\/Work\/memento<\/summary>/);
  assert.match(body, /### Available skills/);
  assert.match(body, /- Session Title: # AGENTS\.md instructions for \/Users\/mandel\/Work\/memento/);
  assert.doesNotMatch(body, /- Session Title:[\s\S]*<INSTRUCTIONS>[\s\S]*<\/INSTRUCTIONS>[\s\S]*- Committer:/);
});

test("truncates note body when still above max limit", () => {
  const longText = "x".repeat(2000);
  const note = `${baseHeader}\n## Conversation\n\n### Mandel\n\n${longText}`;
  const body = buildBody(note, 300);

  assert.match(body, /_Note truncated due to GitHub comment size limits\._/);
  assert.ok(body.length <= 300);
});

test("renders fallback body when no AI session note exists", () => {
  const body = buildNoSessionBody();

  assert.match(body, new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  assert.match(body, /No AI session was attached to this commit\./);
});

test("renders markdown files heading once and at the end section", () => {
  const note = `${baseHeader}
### Codex

# AGENTS.md instructions for /Users/mandel/Work/memento

<INSTRUCTIONS>
## Skills
- git-memento-workflow
</INSTRUCTIONS>

### Mandel

Please review.`;
  const body = buildBody(note, 65000);
  const markdownHeading = "\n\n### Markdown files\n\n";
  const headingIndex = body.indexOf(markdownHeading);

  assert.ok(headingIndex > body.indexOf("<summary>The note attached to the commit</summary>"));
  assert.equal(countMatches(body, /### Markdown files/g), 1);
});

test("ignores markdown blocks inside fenced code and avoids repetitive markdown data", () => {
  const note = `# Git Memento Session

- Provider: Codex
- Session ID: sess-dup
- Session Title: # AGENTS.md instructions for /Users/mandel/Work/memento
<INSTRUCTIONS>
## Skills
- git-memento-workflow
</INSTRUCTIONS>
- Committer: Mandel

## Conversation

### Manuel

\`\`\`markdown
# AGENTS.md instructions for /Users/mandel/Work/memento

<INSTRUCTIONS>
## Skills
A skill is a set of local instructions.
</INSTRUCTIONS>
\`\`\`

\`\`\`markdown
# AGENTS.md instructions for /Users/mandel/Work/memento

<INSTRUCTIONS>
## Skills
  A skill is a set of local instructions.
</INSTRUCTIONS>
\`\`\``;
  const body = buildBody(note, 65000);

  assert.equal(countMatches(body, /<summary>AGENTS\.md instructions for \/Users\/mandel\/Work\/memento<\/summary>/g), 1);
  assert.equal(countMatches(body, /### Markdown files/g), 1);
  assert.equal(countMatches(body, /~~~markdown/g), 1);
});

test("deduplicates markdown sections with the same title across different note locations", () => {
  const note = `# Git Memento Session

- Provider: Codex
- Session ID: sess-same-title
- Session Title: # AGENTS.md instructions for /Users/mandel/Work/memento
<INSTRUCTIONS>
## Skills
- git-memento-workflow
</INSTRUCTIONS>
- Committer: Mandel

## Conversation

### Codex

# AGENTS.md instructions for /Users/mandel/Work/memento

<INSTRUCTIONS>
## Skills
- git-memento-workflow
</INSTRUCTIONS>`;
  const body = buildBody(note, 65000);

  assert.equal(countMatches(body, /<summary>AGENTS\.md instructions for \/Users\/mandel\/Work\/memento<\/summary>/g), 1);
  assert.equal(countMatches(body, /### Markdown files/g), 1);
  assert.equal(countMatches(body, /~~~markdown/g), 1);
});

test("keeps sections with same title when markdown content differs", () => {
  const note = `# Git Memento Session

- Provider: Codex
- Session ID: sess-same-title-different-content
- Session Title: # AGENTS.md instructions for /Users/mandel/Work/memento
<INSTRUCTIONS>
## Skills
- git-memento-workflow
</INSTRUCTIONS>
- Committer: Mandel

## Conversation

### Codex

# AGENTS.md instructions for /Users/mandel/Work/memento

<INSTRUCTIONS>
## How to use skills
- Discovery
</INSTRUCTIONS>`;
  const body = buildBody(note, 65000);

  assert.equal(countMatches(body, /<summary>AGENTS\.md instructions for \/Users\/mandel\/Work\/memento<\/summary>/g), 2);
  assert.equal(countMatches(body, /~~~markdown/g), 2);
  assert.match(body, /## Skills/);
  assert.match(body, /## How to use skills/);
});

test("renders markdown sections with expected details and fenced markdown format", () => {
  const note = `${baseHeader}
### Codex

# PROMPT.md

<INSTRUCTIONS>
## Context
- Keep this markdown.
</INSTRUCTIONS>`;
  const body = buildBody(note, 65000);

  assert.match(body, /### Markdown files\n\n<details>\n<summary>PROMPT\.md<\/summary>\n\n~~~markdown\n[\s\S]*\n~~~\n\n<\/details>/);
});
