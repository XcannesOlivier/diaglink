import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { AiUsagePanel } from '../AiUsagePanel';
import { getUsageSummary, getUsageCompanies, getUsageMachines, getUsageUsers,
  getCompanyUsageSummary, getCompanyUsageMachines, getCompanyUsageUsers } from '../../../services/aiUsageService';
import type { AiUsageMetricsDto } from '../../../types/aiUsage';

vi.mock('../../../services/aiUsageService', () => ({
  getUsageSummary: vi.fn(), getUsageCompanies: vi.fn(), getUsageMachines: vi.fn(), getUsageUsers: vi.fn(),
  getCompanyUsageSummary: vi.fn(), getCompanyUsageMachines: vi.fn(), getCompanyUsageUsers: vi.fn(),
}));
const token = async () => null;
vi.mock('../../../hooks/useAuth', () => ({ useAuth: () => ({ getAccessToken: token }) }));
const metrics: AiUsageMetricsDto = { eventCount: 1, chatResponseCount: 1, conversationSummaryCount: 0, visionToolCount: 0,
  knownUsageCount: 0, unknownUsageCount: 1, completedCount: 0, notCompletedCount: 1,
  inputTokens: null, outputTokens: null, totalTokens: null };
describe('usage panel', () => {
  let host: HTMLDivElement; let root: Root;
  beforeEach(() => {
    vi.clearAllMocks();
    Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
    host = document.createElement('div'); document.body.append(host); root = createRoot(host);
    vi.mocked(getUsageSummary).mockResolvedValue({ kind: 'success', data: {
      from: null, to: '2026-09-09T00:00:00Z', usageType: null, metrics,
      unassignedCompanyCount: 0, unassignedMachineCount: 0, unassignedUserCount: 0,
    } });
    vi.mocked(getUsageCompanies).mockResolvedValue({ kind: 'success', data: [
      { companyId: null, companyName: 'Entreprise non attribuée', machineCountUsed: 1, userCountUsed: 1, metrics },
    ] });
    vi.mocked(getUsageMachines).mockResolvedValue({ kind: 'success', data: [
      { machineId: 'machine-1', machineName: 'Compresseur', userCountUsed: 1, metrics },
    ] });
    vi.mocked(getUsageUsers).mockResolvedValue({ kind: 'success', data: [
      { userId: 'user-1', userDisplayName: 'Utilisateur test', email: null, metrics },
    ] });
  });
  afterEach(async () => { await act(async () => root.unmount()); host.remove(); });
  it('keeps selected company totals and drilldown isolated from platform totals', async () => {
    vi.mocked(getUsageCompanies).mockResolvedValue({kind:'success',data:[
      {companyId:'a',companyName:'Alpha',machineCountUsed:1,userCountUsed:1,metrics:{...metrics,chatResponseCount:7}},
      {companyId:'b',companyName:'Beta',machineCountUsed:1,userCountUsed:1,metrics:{...metrics,chatResponseCount:999}},
    ]});
    await act(async()=>root.render(<AiUsagePanel key="a" selectedCompany={{id:'a',name:'Alpha'}}/>));
    expect(getUsageSummary).not.toHaveBeenCalled();
    expect(getUsageMachines).toHaveBeenLastCalledWith(token,'a',expect.any(Object),expect.any(AbortSignal));
    expect(host.textContent).not.toContain('999');expect(host.textContent).not.toContain('Toutes les entreprises');
    await click('Compresseur');
    expect(getUsageUsers).toHaveBeenLastCalledWith(token,'a','machine-1',expect.any(Object),expect.any(AbortSignal));
    await act(async()=>root.render(<AiUsagePanel key="b" selectedCompany={{id:'b',name:'Beta'}}/>));
    expect(getUsageMachines).toHaveBeenLastCalledWith(token,'b',expect.any(Object),expect.any(AbortSignal));
    expect(host.textContent).not.toContain('Alpha');expect(host.textContent).not.toContain('Utilisateur test');
  });
  const click = async (name: string) => {
    const button = [...host.querySelectorAll('button')].find(b => b.textContent === name);
    expect(button).toBeDefined();
    await act(async () => button!.click());
  };
  it('shows unknown usage and navigates with historical scopes and fixed bounds', async () => {
    await act(async () => root.render(<AiUsagePanel />));
    expect(host.textContent).toContain('Usage inconnu');
    await click('Entreprise non attribuée');
    const window = vi.mocked(getUsageSummary).mock.calls[0][1];
    expect(getUsageMachines).toHaveBeenCalledWith(token, null, window, expect.any(AbortSignal));
    await click('Compresseur');
    expect(getUsageUsers).toHaveBeenCalledWith(token, null, 'machine-1', window, expect.any(AbortSignal));
    expect(host.textContent).toContain('Utilisateur test');
    await click('Retour aux machines');
    expect(host.textContent).toContain('Compresseur');
    await click('Toutes les entreprises');
    expect(host.textContent).toContain('Consommation par entreprise');
  });
  it('company mode starts at machines and never calls the admin API', async () => {
    vi.mocked(getCompanyUsageSummary).mockResolvedValue({ kind: 'success', data: {
      from: null, to: '2026-09-09T00:00:00Z', usageType: null, metrics,
      unassignedCompanyCount: 0, unassignedMachineCount: 0, unassignedUserCount: 0,
    } });
    vi.mocked(getCompanyUsageMachines).mockResolvedValue({ kind: 'success', data: [
      { machineId: 'machine-a', machineName: 'Machine A', userCountUsed: 1, metrics },
    ] });
    vi.mocked(getCompanyUsageUsers).mockResolvedValue({ kind: 'success', data: [
      { userId: null, userDisplayName: 'Utilisateur non attribué', email: null, metrics },
    ] });
    await act(async () => root.render(<AiUsagePanel scope="company" companyName="Entreprise A" />));
    expect(getUsageSummary).not.toHaveBeenCalled();
    expect(getUsageCompanies).not.toHaveBeenCalled();
    expect(host.textContent).toContain('Consommation par machine');
    expect(host.textContent).not.toContain('Toutes les entreprises');
    await click('Machine A');
    const bounds = vi.mocked(getCompanyUsageSummary).mock.calls[0][1];
    expect(getCompanyUsageUsers).toHaveBeenCalledWith(token, 'machine-a', bounds, expect.any(AbortSignal));
    expect(getUsageUsers).not.toHaveBeenCalled();
    expect(host.textContent).toContain('Entreprise A > Machine A > Utilisateurs');
    await click('Retour aux machines');
    expect(host.textContent).toContain('Consommation par machine');
    expect(getUsageMachines).not.toHaveBeenCalled();
  });
  it.each(['super-admin', 'company'] as const)('filters Vision and preserves all totals and navigation in %s mode', async scope => {
    const companyMode = scope === 'company';
    const loadSummary = companyMode ? getCompanyUsageSummary : getUsageSummary;
    const all = { ...metrics, eventCount: 5, chatResponseCount: 1, conversationSummaryCount: 1,
      visionToolCount: 3, knownUsageCount: 4, unknownUsageCount: 1, inputTokens: 400, outputTokens: 80, totalTokens: 480,
      cacheReadInputTokens: 30, cacheCreationInputTokens: 50, cacheCreation5mInputTokens: 20, cacheCreation1hInputTokens: 30 };
    const vision = { ...all, eventCount: 3, chatResponseCount: 0, conversationSummaryCount: 0,
      knownUsageCount: 2, inputTokens: 200, outputTokens: 40, totalTokens: 240 };
    vi.mocked(loadSummary).mockImplementation(async (_, filter) => ({ kind: 'success', data: {
      from: null, to: filter.to, usageType: filter.usageType ?? null,
      metrics: filter.usageType === 'VisionTool' ? vision : all,
      unassignedCompanyCount: 0, unassignedMachineCount: 0, unassignedUserCount: 0,
    } }));
    vi.mocked(getUsageCompanies).mockResolvedValue({ kind: 'success', data: [
      { companyId: 'company-a', companyName: 'Client A', machineCountUsed: 1, userCountUsed: 1, metrics: all },
    ] });
    const machineResult = { kind: 'success' as const, data: [
      { machineId: 'machine-a', machineName: 'Machine A', userCountUsed: 1, metrics: vision },
    ] };
    vi.mocked(getUsageMachines).mockResolvedValue(machineResult);
    vi.mocked(getCompanyUsageMachines).mockResolvedValue(machineResult);
    const userResult = { kind: 'success' as const, data: [
      { userId: 'user-a', userDisplayName: 'User A', email: null, metrics: vision },
    ] };
    vi.mocked(getUsageUsers).mockResolvedValue(userResult);
    vi.mocked(getCompanyUsageUsers).mockResolvedValue(userResult);
    await act(async () => root.render(<AiUsagePanel scope={scope} />));
    const totalCard = () => [...host.querySelectorAll('span')].find(e => e.textContent === 'Tokens connus')?.parentElement;
    expect(totalCard()?.textContent).toContain('480');
    expect([...host.querySelectorAll('span')].find(e => e.textContent === 'Cache lu')?.parentElement?.textContent).toContain('30');
    expect([...host.querySelectorAll('span')].find(e => e.textContent === 'Cache créé')?.parentElement?.textContent).toContain('50');
    expect(host.textContent?.includes('Analyses Vision : 3')).toBe(!companyMode);
    expect([...host.querySelectorAll('th')].some(e => e.textContent === 'Vision')).toBe(!companyMode);
    const select = host.querySelector<HTMLSelectElement>(`select[aria-label="Type d'usage"]`)!;
    expect([...select.options].map(o => o.value)).toEqual(['', 'ChatResponse', 'VisionTool', 'ConversationSummary']);
    await act(async () => { select.value = 'VisionTool'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(vi.mocked(loadSummary).mock.lastCall?.[1].usageType).toBe('VisionTool');
    expect(totalCard()?.textContent).toContain('240');
    if (!companyMode) await click('Client A');
    expect([...host.querySelectorAll('th')].map(e => e.textContent)).toEqual(companyMode
      ? ['Machine', 'Utilisateurs', 'Tokens']
      : ['Machine', 'Réponses IA', 'Vision', 'Résumés', 'Utilisateurs', 'Tokens', 'Usages inconnus']);
    expect([...host.querySelectorAll('tbody td')].map(e => e.textContent)).toEqual(companyMode
      ? ['Machine A', '1', '240 (partiel)']
      : ['Machine A', '0', '3', '0', '1', '240 (partiel)', '1']);
    await click('Machine A');
    const loadUsers = companyMode ? getCompanyUsageUsers : getUsageUsers;
    expect(vi.mocked(loadUsers).mock.lastCall).toContainEqual(expect.objectContaining({ usageType: 'VisionTool' }));
    expect(host.textContent).toContain('User A');
    expect([...host.querySelectorAll('th')].map(e => e.textContent)).toEqual(companyMode
      ? ['Utilisateur', 'Input', 'Cache lu', 'Cache créé', 'Cache 5 min', 'Cache 1 h', 'Output', 'Tokens']
      : ['Utilisateur', 'Réponses IA', 'Vision', 'Résumés', 'Input', 'Cache lu', 'Cache créé', 'Cache 5 min', 'Cache 1 h', 'Output', 'Tokens', 'Usages inconnus']);
    expect([...host.querySelectorAll('tbody td')].map(e => e.textContent)).toEqual(companyMode
      ? ['User A', '200', '30', '50', '20', '30', '40', '240 (partiel)']
      : ['User A', '0', '3', '0', '200', '30', '50', '20', '30', '40', '240 (partiel)', '1']);
    await click('Retour aux machines');
    await act(async () => { select.value = ''; select.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(vi.mocked(loadSummary).mock.lastCall?.[1].usageType).toBeUndefined();
    expect(totalCard()?.textContent).toContain('480');
    if (companyMode) expect(getUsageSummary).not.toHaveBeenCalled();
  });
  it('keeps a list failure explicit without hiding the summary', async () => {
    vi.mocked(getUsageCompanies).mockResolvedValue({ kind: 'error' });
    await act(async () => root.render(<AiUsagePanel />));
    expect(host.textContent).toContain('Impossible de charger le détail');
    expect(host.textContent).toContain('Usage inconnu');
    expect(host.textContent).not.toContain('Aucune consommation');
  });
  it('ignores a late response after refreshing', async () => {
    let finish!: (value: Awaited<ReturnType<typeof getUsageCompanies>>) => void;
    vi.mocked(getUsageCompanies).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    await act(async () => root.render(<AiUsagePanel />));
    await click('Actualiser');
    await act(async () => finish({ kind: 'success', data: [{
      companyId: 'old', companyName: 'Ancienne réponse', machineCountUsed: 0, userCountUsed: 0, metrics,
    }] }));
    expect(host.textContent).not.toContain('Ancienne réponse');
    expect(host.textContent).toContain('Entreprise non attribuée');
  });
});
