import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupportContactDialog } from '../SupportContactDialog';

const mocks = vi.hoisted(() => ({ submitSupportContact: vi.fn() }));
vi.mock('../../../services/supportContactApi', () => ({ submitSupportContact: mocks.submitSupportContact }));

let container: HTMLDivElement;
let root: Root;
const onOpenChange = vi.fn();
const onSessionExpired = vi.fn();

function setMessage(value: string) {
  const fields = document.body.querySelectorAll('textarea[name="message"]');
  const field = fields.item(fields.length - 1);
  if (!(field instanceof HTMLTextAreaElement)) throw new Error('Le champ Message est introuvable.');
  Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(field, value);
  field.dispatchEvent(new Event('input', { bubbles: true }));
}

async function submit() {
  const form = document.body.querySelector('form')!;
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
}

beforeEach(async () => {
  mocks.submitSupportContact.mockReset();
  onOpenChange.mockReset();
  onSessionExpired.mockReset();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root.render(
    <SupportContactDialog
      open
      onOpenChange={onOpenChange}
      getAccessToken={async () => 'token'}
      machineId="machine-42"
      onDiagLinkSessionExpired={onSessionExpired}
    />,
  ));
  await act(async () => undefined);
});

afterEach(async () => {
  await act(async () => root.unmount());
  document.body.replaceChildren();
});

describe('SupportContactDialog', () => {
  it('shows the requested controls and sends the trimmed message with machine context', async () => {
    mocks.submitSupportContact.mockResolvedValue({ kind: 'success', data: { success: true } });
    await act(async () => setMessage('  La presse affiche E42.  '));

    await submit();

    expect(document.body.textContent).toContain('Contacter DiagLink');
    expect(document.body.textContent).toContain('Annuler');
    expect(document.body.textContent).toContain('Envoyer');
    expect(mocks.submitSupportContact).toHaveBeenCalledWith(expect.any(Function), {
      message: 'La presse affiche E42.',
      machineId: 'machine-42',
    });
    expect(document.body.textContent).toContain('Votre message a bien été envoyé.');
    expect((document.body.querySelector('textarea[name="message"]') as HTMLTextAreaElement).value).toBe('');
  });

  it('keeps the message after an error', async () => {
    mocks.submitSupportContact.mockResolvedValue({ kind: 'error' });
    await act(async () => setMessage('Message à conserver'));

    await submit();

    expect(document.body.querySelector('[role="alert"]')?.textContent)
      .toContain('Le message n’a pas pu être envoyé.');
    expect((document.body.querySelector('textarea[name="message"]') as HTMLTextAreaElement).value)
      .toBe('Message à conserver');
  });

  it('blocks duplicate submissions while the first request is pending', async () => {
    let confirm!: () => void;
    mocks.submitSupportContact.mockReturnValue(new Promise(resolve => { confirm = () => resolve({ kind: 'success', data: { success: true } }); }));
    await act(async () => setMessage('Message unique'));

    const form = document.body.querySelector('form')!;
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(mocks.submitSupportContact).toHaveBeenCalledTimes(1);
    expect((document.body.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    await act(async () => {
      confirm();
      await Promise.resolve();
    });
  });

  it('reports an expired DiagLink session', async () => {
    mocks.submitSupportContact.mockResolvedValue({ kind: 'unauthorized', diagLinkSessionExpired: true });
    await act(async () => setMessage('Besoin d’aide'));

    await submit();

    expect(onSessionExpired).toHaveBeenCalledTimes(1);
    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('Votre session a expiré.');
  });
});