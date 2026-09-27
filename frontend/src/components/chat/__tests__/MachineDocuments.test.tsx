import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MachineDocuments } from '../MachineDocuments';

const media = vi.hoisted(() => ({ compact: false }));

vi.mock('../../../hooks/useAuth', () => ({
  useAuth: () => ({ getAccessToken: vi.fn() }),
}));
vi.mock('../../../hooks/useThemeProvider', () => ({
  useMediaQuery: () => media.compact,
}));
vi.mock('../../../services/machineService', () => ({
  getMachineDocuments: vi.fn(),
  openMachineDocument: vi.fn(),
}));

describe('MachineDocuments', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it.each([
    ['desktop', false],
    ['mobile', true],
  ] as const)('uses the Doc Machines label on %s', async (_viewport, compact) => {
    media.compact = compact;

    await act(async () => root.render(<MachineDocuments machineId="machine-42" />));

    const button = container.querySelector<HTMLButtonElement>('button[aria-label="Doc Machines"]');
    expect(button?.textContent).toContain('Doc Machines');
    expect(button?.querySelector('svg')).not.toBeNull();
  });
});