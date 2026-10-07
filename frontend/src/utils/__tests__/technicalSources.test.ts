import { describe, expect, it } from 'vitest';
import type { TechnicalSourceReference } from '../../types/chat';
import { parseTechnicalSources, prepareTechnicalSourceContent } from '../technicalSources';

function source(overrides: Partial<TechnicalSourceReference> = {}): TechnicalSourceReference {
  return {
    id: 123,
    pdfPage: 74,
    displayPage: '72',
    label: 'p. 72',
    startIndex: 9,
    endIndex: 14,
    displayOrder: 0,
    ...overrides,
  };
}

describe('parseTechnicalSources', () => {
  it('validates, deduplicates, and orders the public source payload', () => {
    const result = parseTechnicalSources([
      source({ id: 124, pdfPage: 75, displayPage: '73', label: 'p. 73', startIndex: 22, endIndex: 27, displayOrder: 1 }),
      source(),
      { ...source(), pdfPage: 999 },
      { ...source({ id: 125, displayOrder: 2 }), sourceBlob: 'private/manual.pdf' },
      { ...source({ id: 126 }), pdfPage: 0 },
    ]);

    expect(result.map(item => item.id)).toEqual([123, 124, 125]);
    expect(result[2]).not.toHaveProperty('sourceBlob');
  });

  it('returns an empty list for empty or malformed values', () => {
    expect(parseTechnicalSources(undefined)).toEqual([]);
    expect(parseTechnicalSources([])).toEqual([]);
    expect(parseTechnicalSources([{ id: '123' }])).toEqual([]);
  });
});

describe('prepareTechnicalSourceContent', () => {
  it('prepares one source without changing surrounding accents and apostrophes', () => {
    const text = 'Réglage : p. 72 d’après le manuel.';
    const startIndex = text.indexOf('p. 72');
    const result = prepareTechnicalSourceContent(text, [source({ startIndex, endIndex: startIndex + 5 })]);

    expect(result.preparedSources).toHaveLength(1);
    expect(result.content).toBe(`Réglage : ${result.preparedSources[0].marker} d’après le manuel.`);
  });

  it('uses JavaScript UTF-16 offsets when an emoji precedes multiple sources', () => {
    const text = 'Contrôle 🛠️ : p. 72 puis p. 73.';
    const first = text.indexOf('p. 72');
    const second = text.indexOf('p. 73');
    const result = prepareTechnicalSourceContent(text, [
      source({ startIndex: first, endIndex: first + 5 }),
      source({ id: 124, pdfPage: 75, displayPage: '73', label: 'p. 73', startIndex: second, endIndex: second + 5, displayOrder: 1 }),
    ]);

    expect(first).toBe(15);
    expect(result.preparedSources).toHaveLength(2);
    expect(result.content).toContain(result.preparedSources[0].marker);
    expect(result.content).toContain(result.preparedSources[1].marker);
  });

  it('ignores an invalid range and keeps the original text', () => {
    const text = 'Source : p. 72';
    expect(prepareTechnicalSourceContent(text, [source({ startIndex: 200, endIndex: 205 })]))
      .toEqual({ content: text, preparedSources: [] });
  });

  it('keeps all text normal when valid references overlap', () => {
    const text = 'Source : p. 72';
    const first = source();
    const overlapping = source({ id: 124, label: '. 72', startIndex: 10, endIndex: 14, displayOrder: 1 });

    expect(prepareTechnicalSourceContent(text, [first, overlapping]))
      .toEqual({ content: text, preparedSources: [] });
  });
});