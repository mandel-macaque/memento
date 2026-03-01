"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { buildBody, marker } = require("../tools/note-comment-renderer");

const baseHeader = `# Git Memento Session\n\n- Provider: Codex\n- Session ID: sess-123\n- Committer: Mandel\n`;

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
  assert.match(body, /> Embedded markdown moved below: AGENTS\.md instructions for \/Users\/mandel\/Work\/memento/);
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

test("truncates note body when still above max limit", () => {
  const longText = "x".repeat(2000);
  const note = `${baseHeader}\n## Conversation\n\n### Mandel\n\n${longText}`;
  const body = buildBody(note, 300);

  assert.match(body, /_Note truncated due to GitHub comment size limits\._/);
  assert.ok(body.length <= 300);
});
