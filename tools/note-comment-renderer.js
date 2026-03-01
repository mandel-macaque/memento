"use strict";

const marker = "<!-- git-memento-note-comment -->";
const markdownFileHeadingPattern = /^#{1,6}\s+([^\s]+\.md\b.*)$/i;
const speakerHeadingPattern = /^###\s+/;

const normalizeLineEndings = (value) => (value || "").replace(/\r\n/g, "\n");

const escapeHtml = (value) =>
  (value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");

const parseNote = (note) => {
  const providerMatch = note.match(/^- Provider:\s*(.+)$/m);
  const sessionIdMatch = note.match(/^- Session ID:\s*(.+)$/m);
  const provider = providerMatch ? providerMatch[1].trim() : "unknown";
  const sessionId = sessionIdMatch ? sessionIdMatch[1].trim() : "";
  return { provider, sessionId };
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

const extractMarkdownFileSections = (note) => {
  const lines = normalizeLineEndings(note).split("\n");
  const sections = [];
  const remaining = [];

  for (let i = 0; i < lines.length; i += 1) {
    const currentLine = lines[i];
    const headingMatch = currentLine.trim().match(markdownFileHeadingPattern);

    if (!headingMatch) {
      remaining.push(currentLine);
      continue;
    }

    const title = headingMatch[1].trim();
    let end = i + 1;

    while (end < lines.length) {
      const candidate = lines[end].trim();
      if (/^<\/INSTRUCTIONS>$/i.test(candidate)) {
        end += 1;
        break;
      }

      if (candidate && markdownFileHeadingPattern.test(candidate) && end > i + 1) {
        break;
      }

      if (candidate && speakerHeadingPattern.test(candidate) && end > i + 1) {
        break;
      }

      end += 1;
    }

    const sectionContent = lines.slice(i + 1, end).join("\n").trim();
    sections.push({ title, content: formatMarkdownSectionContent(sectionContent) });
    remaining.push(`> Embedded markdown moved below: ${title}`);
    i = end - 1;
  }

  return {
    noteBody: remaining.join("\n").trim(),
    sections,
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

const buildBody = (note, maxBodyLength) => {
  const { provider, sessionId } = parseNote(note);
  const agentId = sessionId ? `${provider} / ${sessionId}` : provider;
  const normalizedNote = normalizeLineEndings(note).trim();
  const { noteBody, sections } = extractMarkdownFileSections(normalizedNote);
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
  marker,
};
