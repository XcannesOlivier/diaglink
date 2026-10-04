import { describe, expect, it } from 'vitest';
import { parseTechnicalVisuals } from '../technicalVisuals';

describe('parseTechnicalVisuals', () => {
  it('projects, deduplicates by id, and sorts by displayOrder', () => {
    const result = parseTechnicalVisuals([
      { id: 2, documentId: 'manual', page: 2, assetType: 'full', tile: null, name: 'b.png', displayOrder: 2, assetKey: 'private/path' },
      { id: 1, documentId: 'manual', page: 1, assetType: 'tile', tile: 'r01-c01', name: 'a.png', displayOrder: 1 },
      { id: 1, documentId: 'changed', page: 9, assetType: 'full', tile: null, name: 'duplicate.png', displayOrder: 9 },
    ]);

    expect(result.map(item => item.id)).toEqual([1, 2]);
    expect(result[1]).not.toHaveProperty('assetKey');
  });

  it('returns an empty list for absent or malformed values', () => {
    expect(parseTechnicalVisuals(undefined)).toEqual([]);
    expect(parseTechnicalVisuals([{ id: 'bad' }])).toEqual([]);
  });
});
