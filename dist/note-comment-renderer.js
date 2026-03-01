"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.buildBody = exports.buildNoSessionBody = exports.marker = void 0;
const markdown_it_1 = __importDefault(require("markdown-it"));
exports.marker = "<!-- git-memento-note-comment -->";
const sessionEnvelopeMarker = "<!-- git-memento-sessions:v1 -->";
const noteVersionMarker = "<!-- git-memento-note-version:1 -->";
const sessionStartMarker = "<!-- git-memento-session:start -->";
const sessionEndMarker = "<!-- git-memento-session:end -->";
const markdownFileHeadingPattern = /^#{1,6}\s+([^\s]+\.md\b.*)$/i;
const speakerHeadingPattern = /^###\s+(.+?)\s*$/;
const markdownParser = new markdown_it_1.default({ html: true, linkify: false, typographer: false });
const normalizeLineEndings = (value) => value.replace(/\r\n/g, "\n");
const extractSessionNotes = (note) => {
    const normalized = normalizeLineEndings(note).trim();
    if (!normalized) {
        return [];
    }
    const lines = normalized.split("\n");
    const firstNonEmpty = lines.find((line) => line.trim().length > 0);
    if ((firstNonEmpty ?? "").trim() !== sessionEnvelopeMarker) {
        return [normalized];
    }
    const unescapeCollisionLine = (line) => {
        const trimmed = line.trim();
        if (trimmed === `\\${sessionStartMarker}` ||
            trimmed === `\\${sessionEndMarker}` ||
            trimmed === `\\${sessionEnvelopeMarker}` ||
            trimmed === `\\${noteVersionMarker}`) {
            return line.replace(trimmed, trimmed.slice(1));
        }
        return line;
    };
    const sessions = [];
    let collecting = false;
    let current = [];
    for (const line of lines) {
        const trimmed = line.trim();
        if (trimmed === sessionStartMarker) {
            collecting = true;
            current = [];
            continue;
        }
        if (trimmed === sessionEndMarker) {
            if (collecting) {
                const entry = current.map(unescapeCollisionLine).join("\n").trim();
                if (entry) {
                    sessions.push(entry);
                }
            }
            collecting = false;
            current = [];
            continue;
        }
        if (collecting) {
            current.push(line);
        }
    }
    return sessions.length > 0 ? sessions : [normalized];
};
const escapeHtml = (value) => value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
const parseNote = (note) => {
    const providerMatch = note.match(/^- Provider:\s*(.+)$/m);
    const sessionIdMatch = note.match(/^- Session ID:\s*(.+)$/m);
    const committerMatch = note.match(/^- Committer:\s*(.+)$/m);
    return {
        provider: providerMatch ? providerMatch[1].trim() : "unknown",
        sessionId: sessionIdMatch ? sessionIdMatch[1].trim() : "",
        committer: committerMatch ? committerMatch[1].trim() : "",
    };
};
const formatMarkdownSectionContent = (value) => {
    const content = normalizeLineEndings(value).trim();
    if (!content) {
        return "_No content_";
    }
    const nonInstructionLines = content
        .split("\n")
        .map((line) => line.trim())
        .filter((line) => line.length > 0 && !/^<\/?INSTRUCTIONS>$/i.test(line));
    const joined = nonInstructionLines.join(" ");
    const looksFlattened = nonInstructionLines.length > 0 &&
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
const normalizeMarkdownTitle = (value) => value.replace(/^#+\s*/, "").trim();
const getParsedTokens = (value) => {
    try {
        return markdownParser.parse(value, {});
    }
    catch {
        return [];
    }
};
const getFencedLineRanges = (value) => getParsedTokens(value)
    .filter((token) => (token.type === "fence" || token.type === "code_block") &&
    Array.isArray(token.map) &&
    token.map.length === 2 &&
    typeof token.map[0] === "number" &&
    typeof token.map[1] === "number")
    .map((token) => ({ start: token.map[0], end: token.map[1] }));
const isLineInsideRanges = (lineIndex, ranges) => ranges.some((range) => lineIndex >= range.start && lineIndex < range.end);
const getMarkdownFileHeadings = (value) => {
    const tokens = getParsedTokens(value);
    const headings = [];
    for (let i = 0; i < tokens.length; i += 1) {
        const token = tokens[i];
        if (token.type !== "heading_open" || !Array.isArray(token.map) || token.map.length !== 2) {
            continue;
        }
        const inline = tokens[i + 1];
        if (!inline || inline.type !== "inline") {
            continue;
        }
        const line = `# ${inline.content ?? ""}`.trim();
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
    const normalizeForKey = (value) => normalizeLineEndings(value)
        .split("\n")
        // Intentionally ignore blank-line-only differences for dedupe keys.
        // Preserve leading indentation so markdown structure changes (lists/code blocks) stay distinct.
        .map((line) => line.trimEnd())
        .filter((line) => line.trim().length > 0)
        .join("\n")
        .trimEnd();
    const seen = new Set();
    const deduped = [];
    for (const section of sections) {
        const normalizedTitle = normalizeForKey(section.title).toLowerCase().trim();
        const normalizedContent = normalizeForKey(section.content);
        const key = `${normalizedTitle}\n---\n${normalizedContent}`;
        if (!seen.has(key)) {
            seen.add(key);
            deduped.push(section);
        }
    }
    return deduped;
};
const extractMarkdownFileSectionsForProvider = (note, provider, committer) => {
    const normalizedProvider = provider.trim().toLowerCase();
    const providerRenderers = {
        codex: { formatSection: formatMarkdownSectionContent },
        claude: { formatSection: formatMarkdownSectionContent },
    };
    const renderer = providerRenderers[normalizedProvider] ?? { formatSection: (value) => normalizeLineEndings(value).trim() };
    const lines = normalizeLineEndings(note).split("\n");
    const headingEntries = getMarkdownFileHeadings(note).sort((a, b) => a.line - b.line);
    const headingStartLines = new Set(headingEntries.map((entry) => entry.line));
    const fencedRanges = getFencedLineRanges(note);
    const sections = [];
    const speakers = new Set([provider, committer, "System", "Tool"]
        .filter((name) => typeof name === "string" && name.trim().length > 0)
        .map((name) => name.trim().toLowerCase()));
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
    const sessionTitleMatch = note.match(/^- Session Title:\s*(.+)$/m);
    const topLevelFallbackTitle = sessionTitleMatch ? normalizeMarkdownTitle(sessionTitleMatch[1]) : "Session instructions";
    const extractedTopLevel = extractTopLevelInstructionSections(baseNoteBody, topLevelFallbackTitle, renderer.formatSection);
    return {
        noteBody: extractedTopLevel.noteBody,
        sections: dedupeSections([...sections, ...extractedTopLevel.sections]),
    };
};
const renderMarkdownSections = (sections) => {
    if (sections.length === 0) {
        return "";
    }
    const rendered = sections
        .map((section) => `<details>\n<summary>${escapeHtml(section.title)}</summary>\n\n~~~markdown\n${section.content}\n~~~\n\n</details>`)
        .join("\n\n");
    return `\n\n### Markdown files\n\n${rendered}`;
};
const buildNoSessionBody = () => `${exports.marker}\nNo AI session was attached to this commit.`;
exports.buildNoSessionBody = buildNoSessionBody;
const buildBody = (note, maxBodyLength) => {
    const normalizedNote = normalizeLineEndings(note).trim();
    const sessionNotes = extractSessionNotes(normalizedNote);
    const renderedSessions = sessionNotes.map((sessionNote, index) => {
        const { provider, sessionId, committer } = parseNote(sessionNote);
        const agentId = sessionId ? `${provider} / ${sessionId}` : provider;
        const { noteBody, sections } = extractMarkdownFileSectionsForProvider(sessionNote, provider, committer);
        const renderedNote = noteBody || sessionNote;
        return { index, agentId, renderedNote, sections };
    });
    if (renderedSessions.length === 0) {
        return (0, exports.buildNoSessionBody)();
    }
    const isSingleSession = renderedSessions.length === 1;
    const agents = [...new Set(renderedSessions.map((session) => session.agentId).filter(Boolean))];
    const heading = isSingleSession
        ? `${exports.marker}\nThis commit has a prompt attached to it created with agent ${agents[0]}:`
        : `${exports.marker}\nThis commit has prompts attached to it created with agents ${agents.join(", ")}:`;
    const renderedNote = isSingleSession
        ? renderedSessions[0].renderedNote
        : renderedSessions
            .map((session) => `## Session ${session.index + 1}: ${session.agentId}\n\n${session.renderedNote}`)
            .join("\n\n");
    const allSections = isSingleSession
        ? renderedSessions[0].sections
        : renderedSessions.flatMap((session) => session.sections.map((section) => ({
            title: `Session ${session.index + 1} - ${section.title}`,
            content: section.content,
        })));
    const renderedSections = renderMarkdownSections(allSections);
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
exports.buildBody = buildBody;
