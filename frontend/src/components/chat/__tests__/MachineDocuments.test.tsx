import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MachineDocuments } from '../MachineDocuments';

const media = vi.hoisted(() => ({ compact: false, mobile: false, touch: false }));
const api = vi.hoisted(() => ({ get: vi.fn(), open: vi.fn() }));

vi.mock('../../../hooks/useAuth', () => ({
  useAuth: () => ({ getAccessToken: vi.fn() }),
}));
vi.mock('../../../hooks/useThemeProvider', () => ({
  useMediaQuery: (query: string) => query.includes('pointer') || query.includes('hover') ? media.touch : query.includes('480px') ? media.mobile : media.compact,
}));
vi.mock('../../../services/machineService', () => ({
  getMachineDocuments: api.get,
  openMachineDocument: api.open,
}));

describe('MachineDocuments', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    media.compact = false;
    media.mobile = false;
    media.touch = false;
    api.get.mockReset().mockResolvedValue({ kind: 'success', data: [] });
    api.open.mockReset();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it.each([
    ['desktop', false, false, 'Doc Machines'],
    ['tablet', true, false, 'Doc Machines'],
    ['mobile', true, true, 'Docs'],
  ] as const)('uses the responsive documents label on %s', async (_viewport, compact, mobile, label) => {
    media.compact = compact;
    media.mobile = mobile;

    await act(async () => root.render(<MachineDocuments machineId="machine-42" />));

    const button = container.querySelector<HTMLButtonElement>('button[aria-label="Documents de la machine"]');
    expect(button?.textContent).toBe(label);
    expect(button?.querySelector('svg')).not.toBeNull();
  });

  it('keeps the accessible name but never renders a visual tooltip on touch devices', async () => {
    media.compact = true;
    media.mobile = true;
    media.touch = true;
    await act(async () => root.render(<MachineDocuments machineId="machine-42" />));

    const button = container.querySelector<HTMLButtonElement>('button[aria-label="Documents de la machine"]')!;
    expect(button.textContent).toBe('Docs');
    expect(document.body.querySelector('[role="tooltip"]')).toBeNull();

    await act(async () => button.click());
    expect(document.body.querySelector('[class*="fui-PopoverSurface"]')).not.toBeNull();
    expect(document.body.querySelector('[role="tooltip"]')).toBeNull();
    expect(api.get).toHaveBeenCalledWith(expect.any(Function), 'machine-42', expect.any(AbortSignal));

    await act(async () => button.click());
    expect(document.body.querySelector('[class*="fui-PopoverSurface"]')).toBeNull();
    await act(async () => button.click());
    expect(document.body.querySelector('[class*="fui-PopoverSurface"]')).not.toBeNull();
    expect(document.body.querySelector('[role="tooltip"]')).toBeNull();
  });

  it('keeps the desktop mouse tooltip and hides it immediately when documents open', async () => {
    vi.useFakeTimers();
    try {
      await act(async () => root.render(<MachineDocuments machineId="machine-42" />));
      const button = container.querySelector<HTMLButtonElement>('button[aria-label="Documents de la machine"]')!;
      await act(async () => {
        button.dispatchEvent(new MouseEvent('pointerover', { bubbles: true }));
        await vi.advanceTimersByTimeAsync(300);
      });
      expect(document.body.querySelector('[role="tooltip"]')?.textContent).toBe('Documents de la machine');

      await act(async () => button.click());
      expect(document.body.querySelector('[class*="fui-PopoverSurface"]')).not.toBeNull();
      expect(document.body.querySelector('[role="tooltip"]')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });
});
