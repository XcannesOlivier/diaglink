import { describe, expect, it } from 'vitest';
import { calculateMaximumAuthorizationPrice, calculatePreparationPrice } from './documentPricing';

describe('document preparation pricing estimate', () => {
  it.each([
    [400, 0, 9_990],
    [550, 150, 14_040],
    [1_000, 600, 26_190],
  ])('calculates %i pages', (pages, extraPages, totalPriceCents) => {
    expect(calculatePreparationPrice(pages)).toEqual({
      extraPages,
      extraPriceCents: extraPages * 27,
      totalPriceCents,
    });
  });

  it.each([
    [400, 12_980],
    [550, 17_030],
    [817, 24_239],
  ])('adds the maximum first subscription to %i pages', (pages, expectedCents) => {
    expect(calculateMaximumAuthorizationPrice(pages)).toBe(expectedCents);
  });
});
