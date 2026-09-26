import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ContactSubmissionError } from '../../services/contactApi';
import { ContactPage } from './ContactPage';

const mocks = vi.hoisted(() => ({ submitContact: vi.fn() }));
vi.mock('../../services/contactApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../services/contactApi')>(),
  submitContact: mocks.submitContact,
}));
vi.mock('../../components/marketing/PublicHeader', () => ({ PublicHeader: () => <header>DiagLink</header> }));
vi.mock('../../components/marketing/ClosingSections', () => ({ PublicFooter: () => <footer>Footer</footer> }));

let container: HTMLDivElement;
let root: Root;

function setValue(name: string, value: string) {
  const field = container.querySelector(`[name="${name}"]`) as HTMLInputElement | HTMLTextAreaElement;
  const prototype = field instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, 'value')?.set?.call(field, value);
  field.dispatchEvent(new Event('input', { bubbles: true }));
}

async function fillAndSubmit() {
  await act(async () => {
    setValue('name', 'Claire Martin');
    setValue('company', 'Ateliers Martin');
    setValue('email', 'claire@example.com');
    setValue('phone', '+33 6 12 34 56 78');
    setValue('message', 'Besoin d’une démonstration.');
  });
  await act(async () => container.querySelector('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
}

beforeEach(async () => {
  mocks.submitContact.mockReset();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root.render(<MemoryRouter><ContactPage isAuthenticated={false} /></MemoryRouter>));
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

describe('ContactPage', () => {
  it('shows a pending state and success only after API confirmation', async () => {
    let confirm!: () => void;
    mocks.submitContact.mockReturnValue(new Promise<void>(resolve => { confirm = resolve; }));

    await fillAndSubmit();

    const pendingButton = container.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(pendingButton.textContent).toBe('Envoi en cours…');
    expect(pendingButton.disabled).toBe(true);
    expect(container.textContent).not.toContain('Votre message a bien été envoyé.');

    await act(async () => confirm());

    expect(container.textContent).toContain('Votre message a bien été envoyé.');
    expect((container.querySelector('[name="name"]') as HTMLInputElement).value).toBe('');
  });

  it('shows the API error and keeps the entered values', async () => {
    mocks.submitContact.mockRejectedValue(new ContactSubmissionError('Le service de contact est indisponible.'));

    await fillAndSubmit();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Le service de contact est indisponible.');
    expect((container.querySelector('[name="email"]') as HTMLInputElement).value).toBe('claire@example.com');
    expect((container.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('blocks duplicate submissions while the first request is pending', async () => {
    let confirm!: () => void;
    mocks.submitContact.mockReturnValue(new Promise<void>(resolve => { confirm = resolve; }));

    await fillAndSubmit();
    await act(async () => container.querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));

    expect(mocks.submitContact).toHaveBeenCalledTimes(1);
    await act(async () => confirm());
  });
});