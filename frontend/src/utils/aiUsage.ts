import type { AiUsageMetricsDto, UsageFilter, UsageType } from '../types/aiUsage';

export const usagePeriods = {
  today: "Aujourd'hui", week: '7 derniers jours', month: 'Ce mois-ci',
  previousMonth: 'Mois précédent', thirtyDays: '30 derniers jours', all: 'Depuis le début',
};
export type UsagePeriod = keyof typeof usagePeriods;
export function usageWindow(period: UsagePeriod, usageType?: UsageType, now = new Date()): UsageFilter {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  let end = new Date(now);
  if (period === 'week') start.setDate(start.getDate() - 6);
  if (period === 'thirtyDays') start.setDate(start.getDate() - 29);
  if (period === 'month') start.setDate(1);
  if (period === 'previousMonth') {
    start.setDate(1);
    end = new Date(start);
    start.setMonth(start.getMonth() - 1);
  }
  return { from: period === 'all' ? undefined : start.toISOString(), to: end.toISOString(), usageType };
}
export const formatUsageTokens = (value: number | null) => value === null ? 'Inconnu' : value.toLocaleString('fr-FR');
export function usageNotice(m: AiUsageMetricsDto): string | null {
  if (!m.unknownUsageCount) return null;
  return m.knownUsageCount ? `Total partiel — ${m.unknownUsageCount} usages inconnus` : 'Usage inconnu';
}

