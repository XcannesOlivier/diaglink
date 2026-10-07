import type { TechnicalSourceReference } from '../types/chat';

export interface PreparedTechnicalSource {
  marker: string;
  source: TechnicalSourceReference;
}

export interface PreparedTechnicalSourceContent {
  content: string;
  preparedSources: PreparedTechnicalSource[];
}

interface MarkdownNode {
  type: string;
  value?: string;
  url?: string;
  children?: MarkdownNode[];
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