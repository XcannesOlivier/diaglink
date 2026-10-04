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
          { id: 'c1', title: 'Conv 1', createdAt: 1 },
          { id: 'c2', title: 'Conv 2', createdAt: 2 },
        ],
        hasMore: true,
      };
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockResponse),
      }));

      const result = await chatService.listConversations();

      expect(result.conversations).toHaveLength(2);
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
      const mockMessages = [{ role: 'user', content: 'Hello' }];
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
      expect(result).toEqual(mockMessages);
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

    it('keeps ordered visuals, removes duplicate ids, and supports legacy messages', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve([
          { role: 'assistant', content: 'Avec image', visuals: [
            { id: 2, documentId: 'manual', page: 72, assetType: 'full', tile: null, name: 'b.png', displayOrder: 1, assetKey: 'secret' },
            { id: 1, documentId: 'manual', page: 71, assetType: 'tile', tile: 'r02-c01', name: 'a.png', displayOrder: 0 },
            { id: 1, documentId: 'other', page: 99, assetType: 'full', tile: null, name: 'duplicate.png', displayOrder: 3 },
          ] },
          { role: 'assistant', content: 'Ancien message' },
        ]),
      }));

      const result = await chatService.getConversationMessages('conv');

      expect(result[0].visuals?.map(visual => visual.id)).toEqual([1, 2]);
      expect(result[0].visuals?.[1]).not.toHaveProperty('assetKey');
      expect(result[1].visuals).toBeUndefined();
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
});
