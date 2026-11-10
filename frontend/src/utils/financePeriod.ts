export type FinancePeriod = 'month' | 'previous' | 'quarter' | 'year' | 'custom';

export function financeBounds(period: FinancePeriod, from: string, to: string, now = new Date()) {
  const year = now.getUTCFullYear(), month = now.getUTCMonth();
  let start: Date, end: Date;
  if (period === 'custom') {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to) || from > to) return null;
    start = new Date(`${from}T00:00:00Z`); end = new Date(`${to}T00:00:00Z`);
    if (!Number.isFinite(+start) || !Number.isFinite(+end) || start.toISOString().slice(0, 10) !== from || end.toISOString().slice(0, 10) !== to) return null;
    end.setUTCDate(end.getUTCDate() + 1);
  } else {
    start = new Date(Date.UTC(year, period === 'year' ? 0 : period === 'previous' ? month - 1 : period === 'quarter' ? month - 2 : month, 1));
    end = period === 'previous' ? new Date(Date.UTC(year, month, 1)) : now;
  }
  return { from: start.toISOString(), to: end.toISOString() };
}
