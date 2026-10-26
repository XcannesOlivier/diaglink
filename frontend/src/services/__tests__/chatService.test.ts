import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ChatService } from '../chatService';
import type { AppAction } from '../../types/appState';
import type { Dispatch } from 'react';

// Mock the auth module
vi.mock('../../config/authConfig', () => ({
  msalConfig: { auth: { clientId: 'test', authority: 'https://login.microsoftonline.com/test' } },
  loginRequest: { scopes: ['api://test/Chat.ReadWrite'] },
  tokenRequest: { scopes: ['api://test/Chat.ReadWrite'], forceRefresh: false },
}));

describe('ChatService', () => {
  let chatService: ChatService;
  let mockDispatch: Dispatch<AppAction>;
  let mockGetAccessToken: () => Promise<string | null>;

  beforeEach(() => {
    vi.restoreAllMocks();
    mockDispatch = vi.fn() as Dispatch<AppAction>;
    mockGetAccessToken = vi.fn().mockResolvedValue('test-token');
    chatService = new ChatService('/api', mockGetAccessToken, mockDispatch);
  });

  it.each([
    ['one photo', [new File(['one'], 'photo.png', { type: 'image/png' })]],
    ['five photos', [
      new File(['1'], '01.png', { type: 'image/png' }),
      new File(['22'], '02.jpg', { type: 'image/jpeg' }),
      new File(['333'], '03.jpeg', { type: 'image/jpeg' }),
      new File(['4444'], '04.png', { type: 'image/png' }),
      new File(['55555'], '05.jpg', { type: 'image/jpeg' }),
    ]],
  ])('restores the text and attachment metadata after HTTP 402 with %s', async (_scenario, files) => {
    const backendMessage = 'CrÃ©dit IA Ã©puisÃ©.';
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ status: 'AiCreditExhausted', message: backendMessage }),
      { status: 402, headers: { 'Content-Type': 'application/json' } },
    ));
    vi.stubGlobal('fetch', fetchMock);

    await expect(chatService.sendMessage('Question avec photos', 'conversation', files))
      .rejects.toMatchObject({ code: 'AiCreditExhausted' });

    const recovery = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction)
      .find((action): action is Extract<AppAction, { type: 'CHAT_RECOVER_MESSAGE' }> =>
        action.type === 'CHAT_RECOVER_MESSAGE');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(recovery).toMatchObject({
      type: 'CHAT_RECOVER_MESSAGE',
      messageText: 'Question avec photos',
      retryCount: 0,
      attachments: files.map(file => ({
        fileName: file.name,
        fileSizeBytes: file.size,
      })),
    });
    expect(recovery?.attachments?.map(attachment => attachment.fileName))
      .toEqual(files.map(file => file.name));
    expect(recovery?.attachments?.every(attachment => attachment.dataUri?.startsWith('data:image/')))
      .toBe(true);
    expect((mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction)
      .some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
  });

  describe('listConversations', () => {
    it('does not retry an exhausted-credit message and history remains readable', async () => {
      const backendMessage = 'Crédit IA épuisé. Rechargez le portefeuille de votre entreprise pour continuer. L’historique reste accessible.';
      const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ status: 'AiCreditExhausted', code: 'aiCreditExhausted', message: backendMessage }), { status: 402 }))
        .mockResolvedValueOnce(new Response(JSON.stringify({ conversations: [], hasMore: false })));
      vi.stubGlobal('fetch', fetchMock);
      await expect(chatService.sendMessage('Question', null, [], 'machine')).rejects.toMatchObject({ code: 'AiCreditExhausted', message: backendMessage });
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(mockDispatch).toHaveBeenCalledWith(expect.objectContaining({ type: 'CHAT_RECOVER_MESSAGE', retryCount: 0, error: expect.objectContaining({ code: 'AiCreditExhausted', message: backendMessage }) }));
      await expect(chatService.listConversations()).resolves.toMatchObject({ conversations: [] });
    });
    it('calls fetch with default limit of 20', async () => {
      const mockResponse = { conversations: [], hasMore: false };
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      });
      vi.stubGlobal('fetch', fetchMock);

      await chatService.listConversations();

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations?limit=20',
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer test-token',
          }),
        }),
      );
    });

    it('calls fetch with custom limit', async () => {
      const mockResponse = { conversations: [], hasMore: false };
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      });
      vi.stubGlobal('fetch', fetchMock);

      await chatService.listConversations(50);

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations?limit=50',
        expect.anything(),
      );
    });

    it('sends the selected machine and abort signal while preserving calls without a machine', async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ conversations: [], hasMore: false }),
      });
      vi.stubGlobal('fetch', fetchMock);
      const controller = new AbortController();

      await chatService.listConversations(50, 'machine/id', controller.signal);

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations?limit=50&machineId=machine%2Fid',
        expect.objectContaining({ signal: controller.signal }),
      );
    });

    it('parses conversations and hasMore from response', async () => {
      const mockResponse = {
        conversations: [
          { id: 'c1', title: 'Conv 1', createdAt: 1791180000, lastActivityAt: 1791446400 },
          { id: 'c2', title: 'Conv 2', createdAt: 1791266400, lastActivityAt: 1791352800 },
        ],
        hasMore: true,
      };
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      }));

      const result = await chatService.listConversations();

      expect(result.conversations).toHaveLength(2);
      expect(result.conversations[0].createdAt).toBe(1791180000);
      expect(result.conversations[0].lastActivityAt).toBe(1791446400);
      expect(result.conversations[0].lastActivityAt).not.toBe(result.conversations[0].createdAt);
      expect(result.hasMore).toBe(true);
    });

    it('throws on non-ok response', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
      }));

      await expect(chatService.listConversations()).rejects.toThrow();
    });
  });

  describe('deleteConversation', () => {
    it('calls DELETE to the correct URL', async () => {
      const fetchMock = vi.fn().mockResolvedValue({ ok: true });
      vi.stubGlobal('fetch', fetchMock);

      await chatService.deleteConversation('conv-123');

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations/conv-123',
        expect.objectContaining({
          method: 'DELETE',
          headers: expect.objectContaining({
            Authorization: 'Bearer test-token',
          }),
        }),
      );
    });

    it('throws on non-ok response', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: false,
        status: 404,
      }));

      await expect(chatService.deleteConversation('conv-123')).rejects.toThrow();
    });
  });

  describe('getConversationMessages', () => {
    it('calls GET to the correct URL', async () => {
      const createdAtUtc = '2026-10-05T14:07:57Z';
      const mockMessages = [{ role: 'user', content: 'Hello', createdAtUtc }];
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockMessages),
      });
      vi.stubGlobal('fetch', fetchMock);

      const result = await chatService.getConversationMessages('conv-123');

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations/conv-123/messages',
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer test-token',
          }),
        }),
      );
      expect(result).toEqual([{ ...mockMessages[0], sources: [] }]);
      expect(result[0].createdAtUtc).toBe(createdAtUtc);
    });

    it('throws on non-ok response', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
      }));

      await expect(chatService.getConversationMessages('conv-123')).rejects.toThrow();
    });

    it('passes an abort signal when loading messages', async () => {
      const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve([]) });
      vi.stubGlobal('fetch', fetchMock);
      const controller = new AbortController();

      await chatService.getConversationMessages('conv-123', controller.signal);

      expect(fetchMock).toHaveBeenCalledWith(
        '/api/conversations/conv-123/messages',
        expect.objectContaining({ signal: controller.signal }),
      );
    });

    it('keeps ordered visuals and sources while supporting legacy messages', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve([
          { role: 'assistant', content: 'Avec image', visuals: [
            { id: 2, documentId: 'manual', page: 72, assetType: 'full', tile: null, name: 'b.png', displayOrder: 1, assetKey: 'secret' },
            { id: 1, documentId: 'manual', page: 71, assetType: 'tile', tile: 'r02-c01', name: 'a.png', displayOrder: 0 },
            { id: 1, documentId: 'other', page: 99, assetType: 'full', tile: null, name: 'duplicate.png', displayOrder: 3 },
          ], sources: [
            { id: 22, pdfPage: 75, displayPage: '73', label: 'p. 73', startIndex: 20, endIndex: 25, displayOrder: 1 },
            { id: 21, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: 8, endIndex: 13, displayOrder: 0, documentId: 'private' },
          ], suggestions: ['Où est le relais ?'], webCitations: [
            { type: 'uri_citation', label: 'Documentation constructeur', url: 'https://manufacturer.example/manuals/2544-9024', quote: 'Caractéristiques techniques' },
            { type: 'uri_citation', label: 'URL invalide', url: 'ftp://manufacturer.example/manual' },
          ] },
          { role: 'assistant', content: 'Ancien message' },
        ]),
      }));

      const result = await chatService.getConversationMessages('conv');

      expect(result[0].visuals?.map(visual => visual.id)).toEqual([1, 2]);
      expect(result[0].visuals?.[1]).not.toHaveProperty('assetKey');
      expect(result[0].sources?.map(source => source.id)).toEqual([21, 22]);
      expect(result[0].sources?.[0]).not.toHaveProperty('documentId');
      expect(result[0].suggestions).toEqual(['Où est le relais ?']);
      expect(result[0].webCitations).toEqual([{
        type: 'uri_citation',
        label: 'Documentation constructeur',
        url: 'https://manufacturer.example/manuals/2544-9024',
        quote: 'Caractéristiques techniques',
      }]);
      expect(result[1].visuals).toBeUndefined();
      expect(result[1].sources).toEqual([]);
      expect(result[1].suggestions).toBeUndefined();
      expect(result[1].webCitations).toBeUndefined();
    });
  });

  it('loads a technical visual through the authenticated id-only endpoint', async () => {
    const blob = new Blob(['png'], { type: 'image/png' });
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, blob: () => Promise.resolve(blob) });
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await expect(chatService.getTechnicalVisualBlob(123, controller.signal)).resolves.toBe(blob);
    expect(fetchMock).toHaveBeenCalledWith('/api/chat/visuals/123', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      signal: controller.signal,
    }));
  });

  it('loads a technical source PDF through the authenticated opaque-id endpoint', async () => {
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, blob: () => Promise.resolve(blob) });
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await expect(chatService.getTechnicalSourcePdfBlob(123, controller.signal)).resolves.toBe(blob);
    expect(fetchMock).toHaveBeenCalledWith('/api/chat/sources/123/document', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      signal: controller.signal,
    }));
  });

  it.each([
    [401, 'session a expiré'],
    [403, 'plus accès'],
    [404, 'plus disponible'],
  ])('maps source endpoint status %i to a safe message', async (status, expected) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status }));

    await expect(chatService.getTechnicalSourcePdfBlob(123)).rejects.toMatchObject({
      message: expect.stringContaining(expected),
    });
  });

  it('maps source endpoint network failures without leaking details', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('private network detail')));

    await expect(chatService.getTechnicalSourcePdfBlob(123)).rejects.toMatchObject({
      message: 'Le document source n’a pas pu être chargé.',
    });
  });

  it('sends image-only UI attachments through imageDataUris without fileDataUris', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      'data: {"type":"done"}\n',
      { status: 200 },
    ));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage(
      'Inspecte cette image',
      null,
      [new File(['png'], 'photo.png', { type: 'image/png' })],
      'machine',
    );

    const request = fetchMock.mock.calls[0][1] as RequestInit;
    const body = JSON.parse(request.body as string) as Record<string, unknown>;
    expect(body.imageDataUris).toEqual([expect.stringMatching(/^data:image\/png;base64,/)]);
    expect(body).not.toHaveProperty('fileDataUris');
  });

  it('rejects chat documents before sending a request', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    await expect(chatService.sendMessage(
      'Analyse le document',
      null,
      [new File(['pdf'], 'manual.pdf', { type: 'application/pdf' })],
      'machine',
    )).rejects.toBeDefined();

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([400, 401, 403, 404, 409, 500])(
    'hides an English HTTP %i detail and never retries the chat request',
    async status => {
      const technicalMessage = `Internal request failed with status ${status}`;
      const fetchMock = vi.fn().mockResolvedValue(new Response(
        JSON.stringify({ message: technicalMessage }),
        { status, headers: { 'Content-Type': 'application/json' } },
      ));
      vi.stubGlobal('fetch', fetchMock);

      await chatService.sendMessage('Question', 'conversation').catch(() => undefined);

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
        .map(call => call[0] as AppAction);
      expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
      const visibleErrors = actions
        .filter((action): action is Extract<AppAction, { type: 'CHAT_ERROR' }> => action.type === 'CHAT_ERROR')
        .map(action => action.error.message);
      expect(visibleErrors.length).toBeGreaterThan(0);
      expect(visibleErrors.join(' ')).not.toContain(technicalMessage);
    },
  );

  it('hides an English SSE error without retrying or completing the interrupted response', async () => {
    const technicalMessage = 'The Claude Messages API request failed.';
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      `data: {"type":"error","message":"${technicalMessage}"}\n\n`,
      { status: 200 },
    ));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question', 'conversation');

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    const visibleErrors = actions
      .filter((action): action is Extract<AppAction, { type: 'CHAT_ERROR' }> => action.type === 'CHAT_ERROR')
      .map(action => action.error.message);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(visibleErrors).toContainEqual(expect.stringContaining('La réponse a été interrompue'));
    expect(visibleErrors.join(' ')).not.toContain(technicalMessage);
    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_STREAM_COMPLETE')).toBe(false);
  });

  it('does not retry an ambiguous network failure before a response is received', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question', 'conversation');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_RECOVER_MESSAGE')).toBe(false);
    expect(actions).toContainEqual(expect.objectContaining({
      type: 'CHAT_ERROR',
      error: expect.objectContaining({
        message: expect.stringContaining('Le traitement a peut-être déjà eu lieu côté serveur'),
      }),
    }));
  });

  it('keeps the received conversation id and attachments without retrying before the first text', async () => {
    const encoder = new TextEncoder();
    let readCount = 0;
    const stream = new ReadableStream<Uint8Array>({
      pull(controller) {
        readCount++;
        if (readCount === 1) {
          controller.enqueue(encoder.encode(
            'data: {"type":"conversationId","conversationId":"new-conversation"}\n\n',
          ));
          return;
        }
        controller.error(new Error('stream failed before first text'));
      },
    }, { highWaterMark: 0 });
    const fetchMock = vi.fn().mockResolvedValue(new Response(stream, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage(
      'Inspecte cette image',
      null,
      [new File(['png'], 'photo.png', { type: 'image/png' })],
      'machine',
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    expect(actions).toContainEqual(expect.objectContaining({
      type: 'CHAT_START_STREAM',
      conversationId: 'new-conversation',
    }));
    expect(actions).toContainEqual(expect.objectContaining({
      type: 'CHAT_SEND_MESSAGE',
      message: expect.objectContaining({
        attachments: [expect.objectContaining({ fileName: 'photo.png' })],
      }),
    }));
    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_RECOVER_MESSAGE')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_STREAM_COMPLETE')).toBe(false);
    expect(actions).toContainEqual(expect.objectContaining({
      type: 'CHAT_ERROR',
      error: expect.objectContaining({
        message: expect.stringContaining('Consultez l’historique de la conversation'),
      }),
    }));
  });

  it('sends an existing conversation exactly once and keeps its request contract', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      'data: {"type":"done"}\n',
      { status: 200 },
    ));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question suivante', 'existing-conversation', [], 'ignored-machine');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(request.body as string)).toMatchObject({
      message: 'Question suivante',
      conversationId: 'existing-conversation',
    });
    expect(JSON.parse(request.body as string)).not.toHaveProperty('machineId');
  });

  it.each([
    ['identical text fragments', ['ha', 'ha'], 'haha'],
    ['different text fragments', ['Bon', 'jour'], 'Bonjour'],
    ['identical numeric fragments', ['12', '12'], '1212'],
    ['identical error-code fragments', ['E1', 'E1'], 'E1E1'],
    ['repeated Markdown characters', ['**', '**'], '****'],
    ['repeated technical references', ['DX10-', 'DX10-'], 'DX10-DX10-'],
    ['accents and Unicode characters', ['é', 'é', '界', '界'], 'éé界界'],
  ])('preserves %s', async (_scenario, fragments, expected) => {
    const stream = [
      ...fragments.map(content => `data: ${JSON.stringify({ type: 'chunk', content })}\n`),
      'data: {"type":"done"}\n',
    ].join('');
    const fetchMock = vi.fn().mockResolvedValue(new Response(stream, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question', 'conversation');

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    const receivedFragments = actions
      .filter((action): action is Extract<AppAction, { type: 'CHAT_STREAM_CHUNK' }> =>
        action.type === 'CHAT_STREAM_CHUNK')
      .map(action => action.content);
    expect(receivedFragments).toEqual(fragments);
    expect(receivedFragments.join('')).toBe(expected);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('preserves identical text around citations and illustrations in event order', async () => {
    const stream = [
      'data: {"type":"chunk","content":"ha"}\n',
      'data: {"type":"annotations","annotations":[{"type":"uri_citation","label":"Manuel","url":"https://manufacturer.example/manual"}]}\n',
      'data: {"type":"chunk","content":"ha"}\n',
      'data: {"type":"visuals","visuals":[{"id":1,"documentId":"manual","page":12,"assetType":"full","tile":null,"name":"page.png","displayOrder":0}]}\n',
      'data: {"type":"chunk","content":"ha"}\n',
      'data: {"type":"done"}\n',
    ].join('');
    const fetchMock = vi.fn().mockResolvedValue(new Response(stream, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question', 'conversation');

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    const streamedActions = actions.filter(action => [
      'CHAT_STREAM_CHUNK',
      'CHAT_STREAM_ANNOTATIONS',
      'CHAT_STREAM_VISUALS',
      'CHAT_STREAM_COMPLETE',
    ].includes(action.type));
    expect(streamedActions.map(action => action.type)).toEqual([
      'CHAT_STREAM_CHUNK',
      'CHAT_STREAM_ANNOTATIONS',
      'CHAT_STREAM_CHUNK',
      'CHAT_STREAM_VISUALS',
      'CHAT_STREAM_CHUNK',
      'CHAT_STREAM_COMPLETE',
    ]);
    expect(streamedActions
      .filter((action): action is Extract<AppAction, { type: 'CHAT_STREAM_CHUNK' }> =>
        action.type === 'CHAT_STREAM_CHUNK')
      .map(action => action.content)
      .join('')).toBe('hahaha');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('dispatches a sanitized visuals SSE event for the streamed assistant message', async () => {
    const stream = [
      'data: {"type":"conversationId","conversationId":"conv"}\n',
      'data: {"type":"visuals","visuals":[{"id":2,"documentId":"manual","page":72,"assetType":"full","tile":null,"name":"b.png","displayOrder":1,"assetKey":"secret"},{"id":1,"documentId":"manual","page":71,"assetType":"tile","tile":"r02-c01","name":"a.png","displayOrder":0},{"id":1,"documentId":"manual","page":71,"assetType":"tile","tile":"r02-c01","name":"a.png","displayOrder":0}]}\n',
      'data: {"type":"done"}\n',
    ].join('');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(stream, { status: 200 })));

    await chatService.sendMessage('Question', null, [], 'machine');

    const visualAction = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction)
      .find(action => action.type === 'CHAT_STREAM_VISUALS');
    expect(visualAction).toMatchObject({ type: 'CHAT_STREAM_VISUALS', visuals: [{ id: 1 }, { id: 2 }] });
    expect(JSON.stringify(visualAction)).not.toContain('assetKey');
  });

  it('keeps streaming through usage, visuals, and sources until done', async () => {
    const stream = [
      'data: {"type":"chunk","content":"Source : p. 72"}\n',
      'data: {"type":"usage","promptTokens":10,"completionTokens":5,"totalTokens":15,"duration":100}\n',
      'data: {"type":"visuals","visuals":[{"id":1,"documentId":"manual","page":74,"assetType":"full","tile":null,"name":"page.png","displayOrder":0}]}\n',
      'data: {"type":"sources","sources":[{"id":123,"pdfPage":74,"displayPage":"72","label":"p. 72","startIndex":9,"endIndex":14,"displayOrder":0}]}\n',
      'data: {"type":"suggestions","suggestions":["Où est le relais ?"]}\n',
      'data: {"type":"done"}\n',
    ].join('');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(stream, { status: 200 })));

    await chatService.sendMessage('Question', 'conv');

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls.map(call => call[0] as AppAction);
    const terminalActions = actions.filter(action =>
      ['CHAT_STREAM_USAGE', 'CHAT_STREAM_VISUALS', 'CHAT_STREAM_SOURCES', 'CHAT_STREAM_SUGGESTIONS', 'CHAT_STREAM_COMPLETE'].includes(action.type));
    expect(terminalActions.map(action => action.type)).toEqual([
      'CHAT_STREAM_USAGE',
      'CHAT_STREAM_VISUALS',
      'CHAT_STREAM_SOURCES',
      'CHAT_STREAM_SUGGESTIONS',
      'CHAT_STREAM_COMPLETE',
    ]);
    expect(terminalActions[0]).toMatchObject({ type: 'CHAT_STREAM_USAGE', messageId: expect.any(String) });
    expect(terminalActions[2]).toMatchObject({ type: 'CHAT_STREAM_SOURCES', sources: [{ id: 123, pdfPage: 74 }] });
    expect(terminalActions[3]).toMatchObject({ type: 'CHAT_STREAM_SUGGESTIONS', suggestions: ['Où est le relais ?'] });
  });

  it('notifies conversation completion only when the done event is received', async () => {
    const onConversationCompleted = vi.fn();
    chatService = new ChatService(
      '/api',
      mockGetAccessToken,
      mockDispatch,
      undefined,
      onConversationCompleted,
    );
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response([
      'data: {"type":"chunk","content":"RÃ©ponse"}\n',
      'data: {"type":"done"}\n',
    ].join(''), { status: 200 })));

    await chatService.sendMessage('Question', 'conversation');

    expect(onConversationCompleted).toHaveBeenCalledTimes(1);
  });
  it('does not retry when the stream fails after a visible chunk', async () => {
    const encoder = new TextEncoder();

    let readCount = 0;

    const stream = new ReadableStream<Uint8Array>(
      {
        pull(controller) {
          readCount++;

          if (readCount === 1) {
            controller.enqueue(
              encoder.encode(
                'data: {"type":"chunk","content":"Réponse partielle"}\n\n'
              )
            );
            return;
          }

          controller.error(new Error('stream failed after partial content'));
        },
      },
      {
        highWaterMark: 0,
      }
    );

    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(stream, { status: 200 }));

    vi.stubGlobal('fetch', fetchMock);

    await chatService.sendMessage('Question', 'conv');

    expect(fetchMock).toHaveBeenCalledTimes(1);

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls.map(
      call => call[0] as AppAction
    );

    expect(actions).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          type: 'CHAT_STREAM_CHUNK',
          content: 'Réponse partielle',
        }),
        expect.objectContaining({
          type: 'CHAT_ERROR',
        }),
      ])
    );

    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_RECOVER_MESSAGE')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_STREAM_COMPLETE')).toBe(false);
    expect(actions).toContainEqual(expect.objectContaining({
      type: 'CHAT_ERROR',
      error: expect.objectContaining({
        message: expect.stringContaining('Le traitement a peut-être déjà eu lieu côté serveur'),
      }),
    }));
  });

  it('cancels an active request without retrying it', async () => {
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => {
        reject(new DOMException('Aborted', 'AbortError'));
      });
    }));
    vi.stubGlobal('fetch', fetchMock);

    const sending = chatService.sendMessage('Question', 'conversation');
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    chatService.cancelStream();
    await sending;

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(actions).toContainEqual({ type: 'CHAT_CANCEL_STREAM' });
    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
  });

  it('keeps streamed conversation data and attachments when cancellation follows partial content', async () => {
    const encoder = new TextEncoder();
    let streamController!: ReadableStreamDefaultController<Uint8Array>;
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        streamController = controller;
        controller.enqueue(encoder.encode([
          'data: {"type":"conversationId","conversationId":"conversation-partial"}\n',
          'data: {"type":"chunk","content":"Réponse partielle"}\n',
          'data: {"type":"annotations","annotations":[{"type":"uri_citation","label":"Manuel","url":"https://example.test/manual"}]}\n',
          'data: {"type":"visuals","visuals":[{"id":7,"documentId":"manual","page":12,"assetType":"full","tile":null,"name":"page.png","displayOrder":0}]}\n',
        ].join('')));
      },
    });
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
      init?.signal?.addEventListener('abort', () => {
        streamController.error(new DOMException('Aborted', 'AbortError'));
      });
      return Promise.resolve(new Response(stream, { status: 200 }));
    });
    vi.stubGlobal('fetch', fetchMock);

    const sending = chatService.sendMessage(
      'Inspecte cette image',
      null,
      [new File(['png'], 'photo.png', { type: 'image/png' })],
      'machine',
    );
    await vi.waitFor(() => expect(mockDispatch).toHaveBeenCalledWith(expect.objectContaining({
      type: 'CHAT_STREAM_CHUNK',
      content: 'Réponse partielle',
    })));
    chatService.cancelStream();
    await sending;

    const actions = (mockDispatch as ReturnType<typeof vi.fn>).mock.calls
      .map(call => call[0] as AppAction);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(actions).toEqual(expect.arrayContaining([
      expect.objectContaining({ type: 'CHAT_START_STREAM', conversationId: 'conversation-partial' }),
      expect.objectContaining({ type: 'CHAT_STREAM_CHUNK', content: 'Réponse partielle' }),
      expect.objectContaining({ type: 'CHAT_STREAM_ANNOTATIONS' }),
      expect.objectContaining({ type: 'CHAT_STREAM_VISUALS', visuals: [expect.objectContaining({ id: 7 })] }),
      expect.objectContaining({
        type: 'CHAT_SEND_MESSAGE',
        message: expect.objectContaining({
          attachments: [expect.objectContaining({ fileName: 'photo.png' })],
        }),
      }),
      { type: 'CHAT_CANCEL_STREAM' },
    ]));
    expect(actions.some(action => action.type === 'CHAT_STREAM_RETRY')).toBe(false);
    expect(actions.some(action => action.type === 'CHAT_STREAM_COMPLETE')).toBe(false);
  });
});
