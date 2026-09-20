import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createRoot } from 'react-dom/client';
import { act } from 'react';
import { CompanyView } from '../CompanyView';

const { getCompanyMock } = vi.hoisted(() => ({ getCompanyMock: vi.fn() }));

vi.mock('../../../services/companyService', () => ({
  getCompany: getCompanyMock,
}));
vi.mock('../CompanyFinancePanel', () => ({ CompanyFinancePanel: () => <div>Finances client</div> }));

describe('CompanyView', () => {
  beforeEach(() => {
    getCompanyMock.mockReset();
  });

  it("affiche le nom et la consommation de l'entreprise", async () => {
    getCompanyMock.mockResolvedValue({ kind: 'success', data: { id: 'c1', name: 'Acme Corp', status: 'active' } });

    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    await act(async () => {
      root.render(<CompanyView getAccessToken={vi.fn().mockResolvedValue('token')} />);
    });

    expect(container.textContent).toContain('Acme Corp');
    expect(container.textContent).toContain('Finances client');
    await act(async () => root.unmount());
    container.remove();
  });
});
