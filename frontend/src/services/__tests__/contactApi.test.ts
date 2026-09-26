import { afterEach, describe, expect, it, vi } from 'vitest';
import { ContactSubmissionError, submitContact, type ContactFormValues } from '../contactApi';

const values: ContactFormValues = {
  name: 'Claire Martin',
  company: 'Ateliers Martin',
  email: 'claire@example.com',
  phone: '+33 6 12 34 56 78',
  message: 'Besoin d’une démonstration.',
};

afterEach(() => vi.restoreAllMocks());

describe('submitContact', () => {
  it('posts the contact payload and resolves only after API confirmation', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ success: true }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await submitContact(values);

    expect(fetchMock).toHaveBeenCalledWith('/api/public/contact', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    });
  });

  it('rejects a successful HTTP response without delivery confirmation', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ success: false }), { status: 200 }));

    await expect(submitContact(values)).rejects.toThrow('Le service n’a pas confirmé l’envoi');
  });

  it('uses safe validation and rate-limit messages returned by the API', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ errors: { email: ['L’adresse e-mail est invalide.'] } }), { status: 400 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ error: 'Trop de messages ont été envoyés. Veuillez réessayer plus tard.' }), { status: 429 }));

    await expect(submitContact(values)).rejects.toThrow('L’adresse e-mail est invalide.');
    await expect(submitContact(values)).rejects.toThrow('Trop de messages ont été envoyés.');
  });

  it('returns a clear message when the network is unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('network details'));

    await expect(submitContact(values)).rejects.toBeInstanceOf(ContactSubmissionError);
    await expect(submitContact(values)).rejects.toThrow('Impossible de contacter le service.');
  });
});