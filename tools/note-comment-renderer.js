"use strict";

const MarkdownIt = require("markdown-it");

const marker = "<!-- git-memento-note-comment -->";
const markdownFileHeadingPattern = /^#{1,6}\s+([^\s]+\.md\b.*)$/i;
const speakerHeadingPattern = /^###\s+(.+?)\s*$/;
const markdownParser = new MarkdownIt({ html: true, linkify: false, typographer: false });

const normalizeLineEndings = (value) => (value || "").replace(/\r\n/g, "\n");

const escapeHtml = (value) =>
  (value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");

const parseNote = (note) => {
  const providerMatch = note.match(/^- Provider:\s*(.+)$/m);
  const sessionIdMatch = note.match(/^- Session ID:\s*(.+)$/m);
  const committerMatch = note.match(/^- Committer:\s*(.+)$/m);
  const provider = providerMatch ? providerMatch[1].trim() : "unknown";
  const sessionId = sessionIdMatch ? sessionIdMatch[1].trim() : "";
  const committer = committerMatch ? committerMatch[1].trim() : "";
  return { provider, sessionId, committer };
};

const formatMarkdownSectionContent = (value) => {
  const content = normalizeLineEndings(value).trim();
  if (!content) {
    return "_No content_";
  }

  const nonInstructionLines = content
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line && !/^<\/?INSTRUCTIONS>$/i.test(line));
  const joined = nonInstructionLines.join(" ");

  const looksFlattened =
    nonInstructionLines.length > 0 &&
    nonInstructionLines.length <= 2 &&
    (/(#{1,6}\s)|(\s-\s)|(\s\d+\)\s)|(<\/?INSTRUCTIONS>)/.test(joined) || joined.includes("SKILL.md"));

  if (!looksFlattened) {
    return content;
  }

  return content
    .replace(/\s*(<\/?INSTRUCTIONS>)\s*/gi, "\n$1\n")
    .replace(/\s+(#{1,6}\s+)/g, "\n\n$1")
    .replace(/\s+(-\s+)/g, "\n$1")
    .replace(/:\s+(\d+\)\s)/g, ":\n$1")
    .replace(/\s+(\d+\)\s)/g, "\n$1")
    .replace(/^(#{1,6}\s+[^\n]+?)\s+(A|An|The)\s+/gm, "$1\n$2 ")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
};

const isConversationSpeakerHeading = (line, speakers) => {
  const match = line.trim().match(speakerHeadingPattern);
  if (!match) {
    return false;
  }

  return speakers.has(match[1].trim().toLowerCase());
};

const normalizeMarkdownTitle = (value) => (value || "").replace(/^#+\s*/, "").trim();

const getParsedTokens = (value) => {
  try {
    return markdownParser.parse(value || "", {});
  } catch {
    return [];
  }
};

const getFencedLineRanges = (value) =>
  getParsedTokens(value)
    .filter((token) => (token.type === "fence" || token.type === "code_block") && token.map && token.map.length === 2)
    .map((token) => ({ start: token.map[0], end: token.map[1] }));

const isLineInsideRanges = (lineIndex, ranges) => ranges.some((range) => lineIndex >= range.start && lineIndex < range.end);

const getMarkdownFileHeadings = (value) => {
  const tokens = getParsedTokens(value);
  const headings = [];

  for (let i = 0; i < tokens.length; i += 1) {
    const token = tokens[i];
    if (token.type !== "heading_open" || !token.map || token.map.length !== 2) {
      continue;
    }

    const inline = tokens[i + 1];
    if (!inline || inline.type !== "inline") {
      continue;
    }

    const line = `# ${inline.content || ""}`.trim();
    const headingMatch = line.match(markdownFileHeadingPattern);
    if (!headingMatch) {
      continue;
    }

    headings.push({ line: token.map[0], title: headingMatch[1].trim() });
  }

  return headings;
};

const resolveSectionTitle = (lines, startIndex, fallbackTitle) => {
  for (let i = startIndex - 1; i >= 0; i -= 1) {
    const line = lines[i].trim();
    if (!line) {
      continue;
    }

    const headingMatch = line.match(markdownFileHeadingPattern);
    if (headingMatch) {
      return headingMatch[1].trim();
    }

    const sessionTitleMatch = line.match(/^- Session Title:\s*(.+)$/i);
    if (sessionTitleMatch) {
      const sessionTitle = normalizeMarkdownTitle(sessionTitleMatch[1]);
      if (sessionTitle) {
        return sessionTitle;
      }
    }

    break;
  }

  return fallbackTitle;
};

const extractTopLevelInstructionSections = (noteBody, fallbackTitle, formatSection) => {
  const lines = normalizeLineEndings(noteBody).split("\n");
  const sections = [];
  const remaining = [];
  const fencedRanges = getFencedLineRanges(noteBody);

  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i];
    const trimmed = line.trim();
    if (isLineInsideRanges(i, fencedRanges)) {
      remaining.push(line);
      continue;
    }

    if (!/^<INSTRUCTIONS>$/i.test(trimmed)) {
      remaining.push(line);
      continue;
    }

    let end = i + 1;
    while (end < lines.length && !/^<\/INSTRUCTIONS>$/i.test(lines[end].trim())) {
      end += 1;
    }

    if (end >= lines.length) {
      // Preserve malformed blocks to avoid data loss.
      remaining.push(line);
      continue;
    }

    const content = lines.slice(i, end + 1).join("\n");
    sections.push({
      title: resolveSectionTitle(lines, i, fallbackTitle),
      content: formatSection(content),
    });
    i = end;
  }

  return {
    noteBody: remaining.join("\n").trim(),
    sections,
  };
};

const dedupeSections = (sections) => {
  const normalizeForKey = (value) =>
    normalizeLineEndings(value)
      .split("\n")
      .map((line) => line.trimEnd())
      .join("\n")
      .replace(/\n{3,}/g, "\n\n")
      .trim();

  const seen = new Set();
  const deduped = [];
  for (const section of sections) {
    const key = `${normalizeForKey(section.title).toLowerCase()}\n---\n${normalizeForKey(section.content)}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    deduped.push(section);
  }
  return deduped;
};

const extractMarkdownFileSectionsForProvider = (note, provider, committer) => {
  const normalizedProvider = (provider || "").trim().toLowerCase();
  const providerRenderers = {
    codex: { formatSection: formatMarkdownSectionContent },
    claude: { formatSection: formatMarkdownSectionContent },
  };
  const renderer = providerRenderers[normalizedProvider] || { formatSection: (value) => normalizeLineEndings(value).trim() };

  const lines = normalizeLineEndings(note).split("\n");
  const headingEntries = getMarkdownFileHeadings(note).sort((a, b) => a.line - b.line);
  const headingStartLines = new Set(headingEntries.map((entry) => entry.line));
  const fencedRanges = getFencedLineRanges(note);
  const sections = [];
  const speakers = new Set(
    [provider, committer, "System", "Tool"]
      .filter(Boolean)
      .map((name) => name.trim().toLowerCase()),
  );
  const keepLine = Array.from({ length: lines.length }, () => true);

  for (const heading of headingEntries) {
    const start = heading.line;
    const title = heading.title;
    let end = start + 1;
    let insideInstructions = false;
    let hadInstructions = false;

    while (end < lines.length) {
      const candidate = lines[end].trim();
      if (isLineInsideRanges(end, fencedRanges)) {
        end += 1;
        continue;
      }

      if (/^<INSTRUCTIONS>$/i.test(candidate)) {
        insideInstructions = true;
        hadInstructions = true;
        end += 1;
        continue;
      }

      if (insideInstructions && /^<\/INSTRUCTIONS>$/i.test(candidate)) {
        insideInstructions = false;
        end += 1;
        if (hadInstructions) {
          break;
        }
        continue;
      }

      if (!insideInstructions && candidate && headingStartLines.has(end) && end > start + 1) {
        break;
      }

      if (!insideInstructions && candidate && isConversationSpeakerHeading(candidate, speakers) && end > start + 1) {
        break;
      }

      end += 1;
    }

    const sectionContent = lines.slice(start + 1, end).join("\n").trim();
    sections.push({ title, content: renderer.formatSection(sectionContent) });
    for (let lineIndex = start; lineIndex < end; lineIndex += 1) {
      keepLine[lineIndex] = false;
    }
  }

  const remaining = lines.filter((_, index) => keepLine[index]);
  const baseNoteBody = remaining.join("\n").trim();
  const topLevelFallbackTitle = note.match(/^- Session Title:\s*(.+)$/m)
    ? normalizeMarkdownTitle(note.match(/^- Session Title:\s*(.+)$/m)[1])
    : "Session instructions";
  const extractedTopLevel = extractTopLevelInstructionSections(baseNoteBody, topLevelFallbackTitle, renderer.formatSection);

  return {
    noteBody: extractedTopLevel.noteBody,
    sections: dedupeSections([...sections, ...extractedTopLevel.sections]),
  };
};

const renderMarkdownSections = (sections) => {
  if (!sections.length) {
    return "";
  }

  const rendered = sections
    .map(
      (section) =>
        `<details>\n<summary>${escapeHtml(section.title)}</summary>\n\n~~~markdown\n${section.content}\n~~~\n\n</details>`,
    )
    .join("\n\n");

  return `\n\n### Markdown files\n\n${rendered}`;
};

const buildNoSessionBody = () => `${marker}\nNo AI session was attached to this commit.`;

const buildBody = (note, maxBodyLength) => {
  const { provider, sessionId, committer } = parseNote(note);
  const agentId = sessionId ? `${provider} / ${sessionId}` : provider;
  const normalizedNote = normalizeLineEndings(note).trim();
  const { noteBody, sections } = extractMarkdownFileSectionsForProvider(normalizedNote, provider, committer);
  const renderedNote = noteBody || normalizedNote;
  const renderedSections = renderMarkdownSections(sections);
  const heading = `${marker}\nThis commit has a prompt attached to it created with agent ${agentId}:`;

  let body = `${heading}\n\n<details>\n<summary>The note attached to the commit</summary>\n\n${renderedNote}\n\n</details>${renderedSections}`;

  if (body.length > maxBodyLength) {
    const withoutSections = `${heading}\n\n<details>\n<summary>The note attached to the commit</summary>\n\n${renderedNote}\n\n</details>\n\n_Nested markdown sections omitted due to GitHub comment size limits._`;
    body = withoutSections;
  }

  if (body.length > maxBodyLength) {
    const reserve = "\n\n_Note truncated due to GitHub comment size limits._\n\n</details>";
    const head = `${heading}\n\n<details>\n<summary>The note attached to the commit</summary>\n\n`;
    const available = Math.max(0, maxBodyLength - head.length - reserve.length);
    body = `${head}${renderedNote.slice(0, available)}${reserve}`;
  }

  return body;
};

module.exports = {
  buildBody,
  buildNoSessionBody,
  marker,
};
