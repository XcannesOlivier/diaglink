import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useFormatTimestamp } from '../useFormatTimestamp';

const localDate = (year: number, month: number, day: number, hour: number, minute: number, second = 0) =>
  new Date(year, month - 1, day, hour, minute, second);

describe('useFormatTimestamp', () => {
  let container: HTMLDivElement;
  let root: Root;
  let formatTimestamp: ReturnType<typeof useFormatTimestamp>;

  beforeEach(async () => {
    vi.useFakeTimers();
    vi.setSystemTime(localDate(2026, 10, 8, 14, 27));
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    function Harness() {
      formatTimestamp = useFormatTimestamp();
      return null;
    }

    await act(async () => root.render(<Harness />));
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.useRealTimers();
  });

  it.each([
    [localDate(2026, 10, 8, 14, 26, 40), "à l'instant"],
    [localDate(2026, 10, 8, 14, 26), 'il y a 1 minute'],
    [localDate(2026, 10, 8, 13, 52), 'il y a 35 minutes'],
    [localDate(2026, 10, 8, 13, 27), 'il y a 1 heure'],
    [localDate(2026, 10, 8, 8, 27), 'il y a 6 heures'],
  ])('formats same-day relative time for %s', (date, expected) => {
    expect(formatTimestamp(date)).toBe(expected);
  });

  it('uses the previous local calendar day around midnight', () => {
    vi.setSystemTime(localDate(2026, 10, 8, 0, 15));

    expect(formatTimestamp(localDate(2026, 10, 7, 23, 50))).toBe('Hier à 23:50');
    expect(formatTimestamp(localDate(2026, 10, 8, 0, 0))).toBe('il y a 15 minutes');
  });

  it('formats an earlier time yesterday', () => {
    expect(formatTimestamp(localDate(2026, 10, 7, 9, 5))).toBe('Hier à 09:05');
  });

  it.each([
    [localDate(2026, 10, 6, 14, 27), '6 oct. à 14:27'],
    [localDate(2026, 2, 3, 8, 4), '3 févr. à 08:04'],
    [localDate(2025, 10, 6, 14, 27), '6 oct. 2025 à 14:27'],
  ])('formats calendar dates for %s', (date, expected) => {
    expect(formatTimestamp(date)).toBe(expected);
  });
});
