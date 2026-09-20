import { describe, it, expect } from 'vitest';
import { usageWindow, usageNotice, formatUsageTokens } from '../aiUsage';
import type { AiUsageMetricsDto } from '../../types/aiUsage';
describe('usage periods and unknown tokens', () => {
  const now = new Date(2026, 8, 8, 14, 30);
  it('uses local calendar boundaries and one upper bound', () => {
    expect(usageWindow('today', undefined, now).from).toBe(new Date(2026, 8, 8).toISOString());
    expect(usageWindow('week', undefined, now).from).toBe(new Date(2026, 8, 2).toISOString());
    expect(usageWindow('thirtyDays', undefined, now).from).toBe(new Date(2026, 7, 10).toISOString());
    expect(usageWindow('month', undefined, now).from).toBe(new Date(2026, 8, 1).toISOString());
    expect(usageWindow('previousMonth', undefined, now)).toEqual({
      from: new Date(2026, 7, 1).toISOString(), to: new Date(2026, 8, 1).toISOString(), usageType: undefined,
    });
    expect(usageWindow('all', 'ChatResponse', now)).toEqual({ from: undefined, to: now.toISOString(), usageType: 'ChatResponse' });
  });
  it('keeps unknown values distinct from measured zero', () => {
    expect(formatUsageTokens(null)).toBe('Inconnu');
    expect(formatUsageTokens(0)).toBe('0');
    expect(usageNotice({ unknownUsageCount: 1, knownUsageCount: 0 } as AiUsageMetricsDto)).toBe('Usage inconnu');
    expect(usageNotice({ unknownUsageCount: 2, knownUsageCount: 1 } as AiUsageMetricsDto)).toBe('Total partiel — 2 usages inconnus');
  });
});

