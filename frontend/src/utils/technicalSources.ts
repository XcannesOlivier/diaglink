import type { TechnicalSourceReference } from '../types/chat';

export interface PreparedTechnicalSource {
  marker: string;
  source: TechnicalSourceReference;
}

export interface PreparedTechnicalSourceContent {
  content: string;
  preparedSources: PreparedTechnicalSource[];
}

export interface CompactedTechnicalSourceContent {
  content: string;
  sources: TechnicalSourceReference[];
}

interface MarkdownNode {
  type: string;
  value?: string;
  url?: string;
  children?: MarkdownNode[];
}

const documentarySourceLinePattern = /^(\s*(?:#{1,6}\s*)?(?:[-*]\s+)?(?:\*\*|__)?\s*(?:📄\s*)?(?:\*\*|__)?\s*(?:sources?|références?)(?:\s+constructeur)?\s*(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*)/iu;
const documentaryScaffoldingPattern = /\bpages?\s*(?:(?:du\s+)?pdf|physiques?|imprim(?:ée|e)s?)?\s*:/giu;

/**
 * Removes only verified references from explicit Claude-generated source lines.
 * Any remaining text is retained as an unverified documentary reference.
 */
export function compactVerifiedSourceLines(
  content: string,
  sources: readonly TechnicalSourceReference[] | undefined,
): CompactedTechnicalSourceContent {
  const exactSources = (sources ?? []).filter(source =>
    source.startIndex >= 0 &&
    source.endIndex > source.startIndex &&
    source.endIndex <= content.length &&
    content.slice(source.startIndex, source.endIndex) === source.label);
  if (exactSources.length === 0) {
    return { content, sources: [...(sources ?? [])] };
  }

  const operations: Array<{ start: number; end: number; replacement: string }> = [];
  const linePattern = /[^\r\n]*(?:\r\n|\r|\n|$)/g;
  for (const match of content.matchAll(linePattern)) {
    const rawLine = match[0];
    if (!rawLine || match.index === undefined) continue;
    const newline = rawLine.match(/(?:\r\n|\r|\n)$/)?.[0] ?? '';
    const line = newline ? rawLine.slice(0, -newline.length) : rawLine;
    const prefix = documentarySourceLinePattern.exec(line);
    if (!prefix) continue;

    const lineStart = match.index;
    const lineEnd = lineStart + line.length;
    const verifiedOnLine = exactSources
      .filter(source => source.startIndex >= lineStart && source.endIndex <= lineEnd)
      .sort((left, right) => right.startIndex - left.startIndex);
    if (verifiedOnLine.length === 0) continue;

    let residual = line;
    for (const source of verifiedOnLine) {
      const start = source.startIndex - lineStart;
      const end = source.endIndex - lineStart;
      residual = residual.slice(0, start) + residual.slice(end);
    }
    residual = residual.slice(prefix[0].length)
      .replace(/\s+/g, ' ')
      .replace(/^\s*[,;·|/–—-]+\s*/u, '')
      .replace(/:\s*[,;·|/–—-]+\s*/gu, ': ')
      .replace(/\s*([,;·])\s*(?=([,;·]|$))/gu, '')
      .trim();

    const substantiveResidual = residual
      .replace(documentaryScaffoldingPattern, '')
      .replace(/[\s,;:·|/\\()[\].–—-]/gu, '');
    const replacement = substantiveResidual.length === 0
      ? ''
      : `📄 Références non vérifiées : ${residual}${newline}`;
    operations.push({ start: lineStart, end: lineStart + rawLine.length, replacement });
  }

  if (operations.length === 0) {
    return { content, sources: [...(sources ?? [])] };
  }

  let compactedContent = content;
  for (const operation of [...operations].sort((left, right) => right.start - left.start)) {
    compactedContent = compactedContent.slice(0, operation.start) +
      operation.replacement +
      compactedContent.slice(operation.end);
  }

  const adjustedSources = (sources ?? []).flatMap(source => {
    if (operations.some(operation =>
      source.startIndex < operation.end && operation.start < source.endIndex)) {
      return [];
    }
    const delta = operations
      .filter(operation => operation.end <= source.startIndex)
      .reduce((total, operation) => total + operation.replacement.length - (operation.end - operation.start), 0);
    return [{
      ...source,
      startIndex: source.startIndex + delta,
      endIndex: source.endIndex + delta,
    }];
  });

  return { content: compactedContent, sources: adjustedSources };
}

/** Projects an untrusted API value onto the public source-reference contract. */
export function parseTechnicalSources(value: unknown): TechnicalSourceReference[] {
  if (!Array.isArray(value)) return [];

  const byId = new Map<number, TechnicalSourceReference>();
  for (const candidate of value) {
    if (!candidate || typeof candidate !== 'object') continue;
    const item = candidate as Record<string, unknown>;
    if (
      typeof item.id !== 'number' || !Number.isSafeInteger(item.id) || item.id <= 0 ||
      (item.documentName !== undefined &&
        (typeof item.documentName !== 'string' ||
          item.documentName.length === 0 ||
          item.documentName.length > 255 ||
          /[\u0000-\u001f\\/]/.test(item.documentName))) ||
      typeof item.pdfPage !== 'number' || !Number.isSafeInteger(item.pdfPage) || item.pdfPage <= 0 ||
      typeof item.displayPage !== 'string' || item.displayPage.length === 0 ||
      typeof item.label !== 'string' || item.label.length === 0 ||
      typeof item.startIndex !== 'number' || !Number.isSafeInteger(item.startIndex) || item.startIndex < 0 ||
      typeof item.endIndex !== 'number' || !Number.isSafeInteger(item.endIndex) || item.endIndex <= item.startIndex ||
      typeof item.displayOrder !== 'number' || !Number.isSafeInteger(item.displayOrder) || item.displayOrder < 0
    ) continue;

    if (!byId.has(item.id)) {
      byId.set(item.id, {
        id: item.id,
        ...(typeof item.documentName === 'string' ? { documentName: item.documentName } : {}),
        pdfPage: item.pdfPage,
        displayPage: item.displayPage,
        label: item.label,
        startIndex: item.startIndex,
        endIndex: item.endIndex,
        displayOrder: item.displayOrder,
      });
    }
  }

  return [...byId.values()].sort((left, right) =>
    left.displayOrder - right.displayOrder || left.startIndex - right.startIndex);
}

/**
 * Replaces verified source labels with collision-free markers before Markdown parsing.
 * The markers are later converted into interactive nodes without recalculating offsets.
 */
export function prepareTechnicalSourceContent(
  content: string,
  sources: readonly TechnicalSourceReference[] | undefined,
): PreparedTechnicalSourceContent {
  const validSources = (sources ?? [])
    .filter(source =>
      source.startIndex >= 0 &&
      source.endIndex > source.startIndex &&
      source.endIndex <= content.length &&
      content.slice(source.startIndex, source.endIndex) === source.label)
    .sort((left, right) => left.startIndex - right.startIndex || left.endIndex - right.endIndex);

  for (let index = 1; index < validSources.length; index += 1) {
    if (validSources[index].startIndex < validSources[index - 1].endIndex) {
      return { content, preparedSources: [] };
    }
  }

  const preparedSources = validSources.map((source, index) => {
    let marker = `DIAGLINKSOURCE${index}ID${source.id}END`;
    while (content.includes(marker)) marker += 'X';
    return { marker, source };
  });

  let preparedContent = content;
  for (let index = preparedSources.length - 1; index >= 0; index -= 1) {
    const { marker, source } = preparedSources[index];
    preparedContent =
      preparedContent.slice(0, source.startIndex) +
      marker +
      preparedContent.slice(source.endIndex);
  }

  return { content: preparedContent, preparedSources };
}

function restoreMarkers(value: string, preparedSources: readonly PreparedTechnicalSource[]): string {
  return preparedSources.reduce(
    (current, prepared) => current.split(prepared.marker).join(prepared.source.label),
    value,
  );
}

function splitTextNode(
  value: string,
  preparedSources: readonly PreparedTechnicalSource[],
  allowLinks: boolean,
): MarkdownNode[] {
  const parts: MarkdownNode[] = [];
  let remaining = value;

  while (remaining.length > 0) {
    const next = preparedSources
      .map(prepared => ({ prepared, index: remaining.indexOf(prepared.marker) }))
      .filter(match => match.index >= 0)
      .sort((left, right) => left.index - right.index)[0];
    if (!next) {
      parts.push({ type: 'text', value: remaining });
      break;
    }

    if (next.index > 0) {
      parts.push({ type: 'text', value: remaining.slice(0, next.index) });
    }
    const sourceText = { type: 'text', value: next.prepared.source.label };
    parts.push(allowLinks
      ? {
          type: 'link',
          url: `#${next.prepared.marker}`,
          children: [sourceText],
        }
      : sourceText);
    remaining = remaining.slice(next.index + next.prepared.marker.length);
  }

  return parts;
}

/** Converts prepared markers to Markdown link nodes while preserving code and existing links. */
export function createTechnicalSourceRemarkPlugin(preparedSources: readonly PreparedTechnicalSource[]) {
  return function technicalSourceRemarkPlugin() {
    return (tree: MarkdownNode) => {
      const visit = (node: MarkdownNode, insideLink: boolean) => {
        if ((node.type === 'code' || node.type === 'inlineCode') && node.value) {
          node.value = restoreMarkers(node.value, preparedSources);
          return;
        }
        if (!node.children) return;

        const nextChildren: MarkdownNode[] = [];
        for (const child of node.children) {
          if (child.type === 'text' && child.value) {
            nextChildren.push(...splitTextNode(
              child.value,
              preparedSources,
              !insideLink && node.type !== 'link',
            ));
          } else {
            visit(child, insideLink || node.type === 'link');
            nextChildren.push(child);
          }
        }
        node.children = nextChildren;
      };

      visit(tree, false);
    };
  };
}
