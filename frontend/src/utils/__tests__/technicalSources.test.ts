import { describe, expect, it } from 'vitest';
import type { TechnicalSourceReference } from '../../types/chat';
import {
  hideFullyVerifiedSourceLinesForDisplay,
  parseTechnicalSources,
  prepareTechnicalSourceContent,
} from '../technicalSources';

function source(overrides: Partial<TechnicalSourceReference> = {}): TechnicalSourceReference {
  return {
    id: 123,
    documentId: 'manual-123',
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
    expect(result[0].documentId).toBe('manual-123');
    expect(result[2]).not.toHaveProperty('sourceBlob');
  });

  it('returns an empty list for empty or malformed values', () => {
    expect(parseTechnicalSources(undefined)).toEqual([]);
    expect(parseTechnicalSources([])).toEqual([]);
    expect(parseTechnicalSources([{ id: '123' }])).toEqual([]);
    expect(parseTechnicalSources([{ ...source(), documentId: '../manual' }])).toEqual([]);
  });
});

describe('hideFullyVerifiedSourceLinesForDisplay', () => {
  function reference(
    content: string,
    label: string,
    overrides: Partial<TechnicalSourceReference> = {},
  ): TechnicalSourceReference {
    const startIndex = content.indexOf(label);
    return source({
      label,
      startIndex,
      endIndex: startIndex + label.length,
      ...overrides,
    });
  }

  it('hides a fully verified source-only line without changing its source offsets', () => {
    const content = 'Diagnostic confirmé.\n📄 Source : Page PDF : 263';
    const verified = reference(content, '263', { pdfPage: 263, displayPage: '263' });
    const original = { ...verified };

    expect(hideFullyVerifiedSourceLinesForDisplay(content, [verified]))
      .toBe('Diagnostic confirmé.\n');
    expect(verified).toEqual(original);
  });

  it('hides several verified references on the same source-only line', () => {
    const content = 'Réponse.\n📄 Sources : p. 58 · p. 59';

    expect(hideFullyVerifiedSourceLinesForDisplay(content, [
      reference(content, 'p. 58', { pdfPage: 58, displayPage: '58' }),
      reference(content, 'p. 59', { id: 124, pdfPage: 59, displayPage: '59' }),
    ])).toBe('Réponse.\n');
  });

  it('keeps commented and unresolved documentary lines intact', () => {
    const commented = '📄 Source : Page PDF : 58 (Fig. 019)';
    const unresolved = '📄 Sources : Page PDF : 58, Page PDF : 999';

    expect(hideFullyVerifiedSourceLinesForDisplay(commented, [
      reference(commented, '58', { pdfPage: 58, displayPage: '58' }),
    ])).toBe(commented);
    expect(hideFullyVerifiedSourceLinesForDisplay(unresolved, [
      reference(unresolved, '58', { pdfPage: 58, displayPage: '58' }),
    ])).toBe(unresolved);
  });

  it('keeps malformed offsets and partially overlapping ranges intact', () => {
    const content = '📄 Source : p. 58';
    const verified = reference(content, 'p. 58', { pdfPage: 58, displayPage: '58' });

    expect(hideFullyVerifiedSourceLinesForDisplay(content, [
      { ...verified, startIndex: 99, endIndex: 104 },
    ])).toBe(content);
    expect(hideFullyVerifiedSourceLinesForDisplay(content, [
      verified,
      { ...verified, id: 124, label: '. 58', startIndex: verified.startIndex + 1 },
    ])).toBe(content);
  });

  it('deduplicates identical ranges without changing the source objects', () => {
    const content = '📄 Source : p. 58';
    const first = reference(content, 'p. 58', { pdfPage: 58, displayPage: '58' });
    const duplicate = { ...first, id: 124 };
    const originals = [{ ...first }, { ...duplicate }];

    expect(hideFullyVerifiedSourceLinesForDisplay(content, [first, duplicate])).toBe('');
    expect([first, duplicate]).toEqual(originals);
  });

  it.each([
    ['backtick fence', '```text\n📄 Source : p. 58\n```'],
    ['tilde fence', '~~~text\n📄 Source : p. 58\n~~~'],
    ['indented block', '    📄 Source : p. 58'],
  ])('does not transform a source line inside a %s', (_name, content) => {
    const verified = reference(content, 'p. 58', { pdfPage: 58, displayPage: '58' });
    expect(hideFullyVerifiedSourceLinesForDisplay(content, [verified])).toBe(content);
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
