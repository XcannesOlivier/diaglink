import { afterEach, describe, expect, it, vi } from 'vitest';
import { getUsageSummary, getUsageUsers, getCompanyUsageSummary, getCompanyUsageMachines, getCompanyUsageUsers } from '../aiUsageService';
vi.mock('../../utils/apiAuth', () => ({
  getApiAuthHeaders: async () => ({ headers: { Authorization: 'Bearer test-only' }, mode: 'microsoft' }),
}));
afterEach(() => vi.unstubAllGlobals());
describe('usage API', () => {
  it('company endpoints never transmit a companyId', async () => {
    const fetcher = vi.fn().mockImplementation(async () => new Response('[]'));
    vi.stubGlobal('fetch', fetcher);
    const filter = { to: '2026-09-09T00:00:00Z' };
    await getCompanyUsageSummary(async () => null, filter);
    await getCompanyUsageMachines(async () => null, filter);
    await getCompanyUsageUsers(async () => null, 'machine-a', filter);
    expect(fetcher).toHaveBeenCalledTimes(3);
    for (const [path] of fetcher.mock.calls) {
      const url = new URL(path, 'https://example.test');
      expect(url.pathname).toMatch(/^\/api\/company\/usage\//);
      expect(url.searchParams.has('companyId')).toBe(false);
    }
  });
  it('transmits the Vision filter for both scopes and preserves its count', async () => {
    const dto = { metrics: { visionToolCount: 3, totalTokens: 42 } };
    const fetcher = vi.fn().mockImplementation(async () => new Response(JSON.stringify(dto)));
    vi.stubGlobal('fetch', fetcher);
    const filter = { to: '2026-09-09T00:00:00Z', usageType: 'VisionTool' as const };
    for (const load of [getUsageSummary, getCompanyUsageSummary]) {
      expect(await load(async () => null, filter)).toEqual({ kind: 'success', data: dto });
    }
    for (const [path, init] of fetcher.mock.calls) {
      expect(new URL(path, 'https://example.test').searchParams.get('usageType')).toBe('VisionTool');
      expect(init.headers.Authorization).toBe('Bearer test-only');
    }
  });
  it('preserves nullable tokens and effective bounds from JSON', async () => {
    const dto = { from: null, to: '2026-09-09T00:00:00Z', usageType: null, metrics: {
      eventCount: 1, chatResponseCount: 1, conversationSummaryCount: 0, visionToolCount: 0, knownUsageCount: 0,
      unknownUsageCount: 1, completedCount: 0, notCompletedCount: 1,
      inputTokens: null, outputTokens: null, totalTokens: null,
    }, unassignedCompanyCount: 1, unassignedMachineCount: 1, unassignedUserCount: 1 };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(dto))));
    expect(await getUsageSummary(async () => null, { to: dto.to })).toEqual({ kind: 'success', data: dto });
  });
  it('sends historical scopes, shared bounds and authentication; preserves API failures', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('', { status: 403 }));
    vi.stubGlobal('fetch', fetcher);
    const result = await getUsageUsers(async () => null, null, 'machine-id', {
      from: '2026-09-01T00:00:00Z', to: '2026-09-09T00:00:00Z', usageType: 'ChatResponse',
    });
    expect(result.kind).toBe('forbidden');
    const [path, init] = fetcher.mock.calls[0];
    const url = new URL(path, 'https://example.test');
    expect(url.searchParams.get('companyId')).toBe('unassigned');
    expect(url.searchParams.get('usageType')).toBe('ChatResponse');
    expect(url.searchParams.get('from')).toBe('2026-09-01T00:00:00Z');
    expect(init.headers.Authorization).toBe('Bearer test-only');
  });
});
