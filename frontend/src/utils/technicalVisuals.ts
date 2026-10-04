import type { TechnicalVisual } from '../types/chat';

/** Projects an untrusted API value onto the public visual contract, then deduplicates and orders it. */
export function parseTechnicalVisuals(value: unknown): TechnicalVisual[] {
  if (!Array.isArray(value)) return [];

  const byId = new Map<number, TechnicalVisual>();
  for (const candidate of value) {
    if (!candidate || typeof candidate !== 'object') continue;
    const item = candidate as Record<string, unknown>;
    if (
      typeof item.id !== 'number' || !Number.isSafeInteger(item.id) || item.id <= 0 ||
      typeof item.documentId !== 'string' ||
      typeof item.page !== 'number' || !Number.isSafeInteger(item.page) || item.page <= 0 ||
      (item.assetType !== 'full' && item.assetType !== 'tile') ||
      (item.tile !== null && typeof item.tile !== 'string') ||
      typeof item.name !== 'string' ||
      typeof item.displayOrder !== 'number' || !Number.isSafeInteger(item.displayOrder)
    ) continue;

    if (!byId.has(item.id)) {
      byId.set(item.id, {
        id: item.id,
        documentId: item.documentId,
        page: item.page,
        assetType: item.assetType,
        tile: item.tile,
        name: item.name,
        displayOrder: item.displayOrder,
      });
    }
  }

  return [...byId.values()].sort((left, right) => left.displayOrder - right.displayOrder);
}
