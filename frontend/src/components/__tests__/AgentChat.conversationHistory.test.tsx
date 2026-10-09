import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentChat } from '../AgentChat';
import type { ConversationMessageInfo, ConversationSummary } from '../../types/appState';

type Role = 'company_admin' | 'diaglink_super_admin';
type Deferred<T> = { promise: Promise<T>; resolve: (value: T) => void };

const deferred = <T,>(): Deferred<T> => {
  let resolve!: (value: T) => void;
  return { promise: new Promise<T>(done => { resolve = done; }), resolve };
};

const mocks = vi.hoisted(() => ({
  dispatch: vi.fn(),
  state: null as unknown as ReturnType<typeof makeState>,
  listConversations: vi.fn(),
  getConversationMessages: vi.fn(),
  sendMessage: vi.fn(),
  onConversationCompleted: undefined as (() => void) | undefined,
}));

const makeState = (
  role: Role,
  machineId = 'machine-a',
  conversations: ConversationSummary[] = [],
  conversationId: string | null = null,
) => ({
  auth: { status: 'authenticated', user: null, error: null, currentUser: { userId: 'user', companyId: 'company', role } },
  chat: { status: 'idle', messages: [], currentConversationId: conversationId, error: null, pendingMessages: [] },
  conversations: { list: conversations, isLoading: false, sidebarOpen: true, hasMore: false },
  ui: { chatInputEnabled: true, currentView: 'chat' },
  machine: { selected: { id: machineId, name: machineId, reference: null } },
});

vi.mock('@fluentui/react-components', () => ({
  Button: ({ children, onClick }: { children?: ReactNode; onClick?: () => void }) => <button type="button" onClick={onClick}>{children}</button>,
  Toaster: () => null,
  Toast: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  ToastTitle: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  useId: () => 'toaster',
  useToastController: () => ({ dispatchToast: vi.fn() }),
}));
vi.mock('../../hooks/useAppState', () => ({ useAppState: () => ({ chat: mocks.state.chat, state: mocks.state }) }));
vi.mock('../../contexts/AppContext', () => ({ useAppContext: () => ({ dispatch: mocks.dispatch }) }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: () => ({ getAccessToken: async () => null }) }));
vi.mock('../../services/chatService', () => ({
  ChatService: class {
    constructor(
      _apiUrl: string,
      _getAccessToken: () => Promise<string | null>,
      _dispatch: unknown,
      _onDiagLinkSessionExpired?: () => void,
      onConversationCompleted?: () => void,
    ) {
      mocks.onConversationCompleted = onConversationCompleted;
    }
    cancelStream() {}
    clearError() {}
    clearChat() {}
    deleteConversation() { return Promise.resolve(); }
    sendMessage(...args: unknown[]) { return mocks.sendMessage(...args); }
    listConversations(limit: number, machineId?: string, signal?: AbortSignal) {
      return mocks.listConversations(limit, machineId, signal);
    }
    getConversationMessages(conversationId: string, signal?: AbortSignal) {
      return mocks.getConversationMessages(conversationId, signal);
    }
  },
}));
vi.mock('../../services/machineService', () => ({ getMachines: vi.fn() }));
vi.mock('../../services/telemetry', () => ({ trackFeedback: vi.fn() }));
vi.mock('../ChatInterface', () => ({
  ChatInterface: ({ onSendMessage }: { onSendMessage: (text: string) => void }) => (
    <button type="button" data-testid="send-message" onClick={() => onSendMessage('Question')}>send</button>
  ),
}));
vi.mock('../ConversationSidebar', () => ({
  ConversationSidebar: ({ conversations, onSelectConversation, onLoadMore }: {
    conversations: ConversationSummary[];
    onSelectConversation: (conversationId: string) => void;
    onLoadMore: () => void;
  }) => <div>
    {conversations.map(conversation => (
      <button
        type="button"
        key={conversation.id}
        data-testid={`conversation-${conversation.id}`}
        onClick={() => onSelectConversation(conversation.id)}
      >{conversation.id}</button>
    ))}
    <button type="button" data-testid="load-more" onClick={onLoadMore}>load more</button>
  </div>,
}));

describe('AgentChat machine-scoped conversation history', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    sessionStorage.setItem('diaglink_machine_entry_choice_handled', 'true');
    mocks.dispatch.mockClear();
    mocks.listConversations.mockReset().mockResolvedValue({ conversations: [], hasMore: false });
    mocks.getConversationMessages.mockReset().mockResolvedValue([]);
    mocks.sendMessage.mockReset().mockImplementation(() => {
      mocks.onConversationCompleted?.();
      return Promise.resolve();
    });
    mocks.onConversationCompleted = undefined;
    mocks.state = makeState('company_admin');
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    sessionStorage.clear();
  });

  const render = async () => act(async () => root.render(<AgentChat agentId="agent" agentName="Assistant" />));

  it.each<Role>(['company_admin', 'diaglink_super_admin'])('requests the selected machine for %s', async role => {
    mocks.state = makeState(role, 'machine-role');
    await render();

    expect(mocks.listConversations).toHaveBeenCalledWith(5, 'machine-role', expect.any(AbortSignal));
  });

  it('clears the list and ignores a late response after switching machines', async () => {
    const responseA = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    const responseB = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    mocks.listConversations.mockImplementation((_limit: number, machineId: string) =>
      machineId === 'machine-a' ? responseA.promise : responseB.promise);

    await render();
    const signalA = mocks.listConversations.mock.calls[0][2] as AbortSignal;
    mocks.state = makeState('company_admin', 'machine-b');
    await render();

    expect(signalA.aborted).toBe(true);
    expect(mocks.dispatch).toHaveBeenCalledWith({ type: 'CONVERSATIONS_SET_LIST', conversations: [], hasMore: false });

    await act(async () => responseB.resolve({
      conversations: [{ id: 'conversation-b', title: 'B', createdAt: 2, lastActivityAt: 2, machineId: 'machine-b', machineName: 'Machine B' }],
      hasMore: false,
    }));
    await act(async () => responseA.resolve({
      conversations: [{ id: 'conversation-a', title: 'A', createdAt: 1, lastActivityAt: 1, machineId: 'machine-a', machineName: 'Machine A' }],
      hasMore: false,
    }));

    const populatedLists = mocks.dispatch.mock.calls
      .map(call => call[0])
      .filter(action => action.type === 'CONVERSATIONS_SET_LIST' && action.conversations.length > 0);
    expect(populatedLists).toEqual([expect.objectContaining({ conversations: [expect.objectContaining({ id: 'conversation-b' })] })]);
  });

  it.each([
    {
      name: 'replaces a reordered prefix without losing the newly active conversation',
      expandedIds: ['F', 'A', 'B', 'C', 'D', 'E', 'G', 'H', 'I', 'J'],
    },
    {
      name: 'replaces an unchanged prefix in its backend order',
      expandedIds: ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J'],
    },
  ])('$name', async ({ expandedIds }) => {
    const summary = (id: string): ConversationSummary => ({
      id,
      title: id,
      createdAt: id.charCodeAt(0),
      lastActivityAt: id.charCodeAt(0),
    });
    const initial = ['A', 'B', 'C', 'D', 'E'].map(summary);
    const expanded = expandedIds.map(summary);
    mocks.state = makeState('company_admin', 'machine-a', initial);
    mocks.listConversations
      .mockResolvedValueOnce({ conversations: initial, hasMore: true })
      .mockResolvedValueOnce({ conversations: expanded, hasMore: false });

    await render();
    await act(async () => host.querySelector<HTMLButtonElement>('[data-testid="load-more"]')?.click());

    expect(mocks.listConversations).toHaveBeenLastCalledWith(10, 'machine-a', expect.any(AbortSignal));
    expect(mocks.dispatch).toHaveBeenLastCalledWith({
      type: 'CONVERSATIONS_SET_LIST',
      conversations: expanded,
      hasMore: false,
    });
  });

  it('refreshes a resumed conversation with the currently loaded prefix size', async () => {
    const summary = (id: string): ConversationSummary => ({
      id,
      title: id,
      createdAt: id.charCodeAt(0),
      lastActivityAt: id.charCodeAt(0),
    });
    const initial = 'ABCDEFGHIJKLMNO'.split('').map(summary);
    const reordered = [initial[14], ...initial.slice(0, 14)];
    mocks.state = makeState('company_admin', 'machine-a', initial, 'O');

    await render();
    mocks.dispatch.mockClear();
    mocks.listConversations.mockClear();
    mocks.listConversations.mockResolvedValueOnce({ conversations: reordered, hasMore: false });
    await act(async () => host.querySelector<HTMLButtonElement>('[data-testid="send-message"]')?.click());

    expect(mocks.listConversations).toHaveBeenCalledWith(15, 'machine-a', expect.any(AbortSignal));
    expect(mocks.dispatch).toHaveBeenLastCalledWith({
      type: 'CONVERSATIONS_SET_LIST',
      conversations: reordered,
      hasMore: false,
    });
  });

  it('uses the initial page size when fewer conversations are currently loaded', async () => {
    const conversations: ConversationSummary[] = [
      { id: 'A', title: 'A', createdAt: 1, lastActivityAt: 1 },
      { id: 'B', title: 'B', createdAt: 2, lastActivityAt: 2 },
    ];
    mocks.state = makeState('company_admin', 'machine-a', conversations, 'B');

    await render();
    mocks.listConversations.mockClear();
    await act(async () => host.querySelector<HTMLButtonElement>('[data-testid="send-message"]')?.click());

    expect(mocks.listConversations).toHaveBeenCalledWith(5, 'machine-a', expect.any(AbortSignal));
  });

  it('ignores a completed refresh response after switching machines', async () => {
    const conversationsA: ConversationSummary[] = [
      { id: 'A', title: 'A', createdAt: 1, lastActivityAt: 1 },
    ];
    const responseA = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    const responseB = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    mocks.state = makeState('company_admin', 'machine-a', conversationsA, 'A');
    await render();
    mocks.dispatch.mockClear();
    mocks.listConversations.mockReset().mockImplementation((_limit: number, machineId: string) =>
      machineId === 'machine-a' ? responseA.promise : responseB.promise);

    await act(async () => host.querySelector<HTMLButtonElement>('[data-testid="send-message"]')?.click());
    const staleSignal = mocks.listConversations.mock.calls[0][2] as AbortSignal;
    mocks.state = makeState('company_admin', 'machine-b');
    await render();

    expect(staleSignal.aborted).toBe(true);
    await act(async () => responseB.resolve({
      conversations: [{ id: 'B', title: 'B', createdAt: 2, lastActivityAt: 2 }],
      hasMore: false,
    }));
    await act(async () => responseA.resolve({ conversations: conversationsA, hasMore: false }));

    const populatedLists = mocks.dispatch.mock.calls
      .map(call => call[0])
      .filter(action => action.type === 'CONVERSATIONS_SET_LIST' && action.conversations.length > 0);
    expect(populatedLists).toEqual([
      expect.objectContaining({ conversations: [expect.objectContaining({ id: 'B' })] }),
    ]);
  });

  it('prevents an older overlapping refresh from replacing a newer result', async () => {
    const first = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    const second = deferred<{ conversations: ConversationSummary[]; hasMore: boolean }>();
    const current: ConversationSummary[] = [
      { id: 'A', title: 'A', createdAt: 1, lastActivityAt: 1 },
    ];
    mocks.state = makeState('company_admin', 'machine-a', current, 'A');
    await render();
    mocks.dispatch.mockClear();
    mocks.listConversations.mockReset()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);

    const sendButton = host.querySelector<HTMLButtonElement>('[data-testid="send-message"]');
    await act(async () => sendButton?.click());
    const firstSignal = mocks.listConversations.mock.calls[0][2] as AbortSignal;
    await act(async () => sendButton?.click());
    expect(firstSignal.aborted).toBe(true);

    await act(async () => second.resolve({
      conversations: [{ id: 'newer', title: 'newer', createdAt: 2, lastActivityAt: 2 }],
      hasMore: false,
    }));
    await act(async () => first.resolve({
      conversations: [{ id: 'older', title: 'older', createdAt: 1, lastActivityAt: 1 }],
      hasMore: false,
    }));

    const populatedLists = mocks.dispatch.mock.calls
      .map(call => call[0])
      .filter(action => action.type === 'CONVERSATIONS_SET_LIST' && action.conversations.length > 0);
    expect(populatedLists).toEqual([
      expect.objectContaining({ conversations: [expect.objectContaining({ id: 'newer' })] }),
    ]);
  });

  it('ignores messages from an earlier conversation click that finishes later', async () => {
    const conversation1 = deferred<ConversationMessageInfo[]>();
    const conversation2 = deferred<ConversationMessageInfo[]>();
    const summaries: ConversationSummary[] = [
      { id: 'conversation-1', title: 'One', createdAt: 1, lastActivityAt: 1, machineId: 'machine-a', machineName: 'Machine A' },
      { id: 'conversation-2', title: 'Two', createdAt: 2, lastActivityAt: 2, machineId: 'machine-a', machineName: 'Machine A' },
    ];
    mocks.state = makeState('diaglink_super_admin', 'machine-a', summaries);
    mocks.getConversationMessages.mockImplementation((conversationId: string) =>
      conversationId === 'conversation-1' ? conversation1.promise : conversation2.promise);
    await render();

    const firstButton = host.querySelector<HTMLButtonElement>('[data-testid="conversation-conversation-1"]')!;
    const secondButton = host.querySelector<HTMLButtonElement>('[data-testid="conversation-conversation-2"]')!;
    await act(async () => firstButton.click());
    const firstSignal = mocks.getConversationMessages.mock.calls[0][1] as AbortSignal;
    await act(async () => secondButton.click());
    expect(firstSignal.aborted).toBe(true);

    await act(async () => conversation2.resolve([{ role: 'assistant', content: 'Réponse 2' }]));
    await act(async () => conversation1.resolve([{ role: 'assistant', content: 'Réponse 1' }]));

    const loaded = mocks.dispatch.mock.calls.map(call => call[0]).filter(action => action.type === 'CHAT_LOAD_CONVERSATION');
    expect(loaded).toEqual([expect.objectContaining({
      conversationId: 'conversation-2',
      messages: [expect.objectContaining({ content: 'Réponse 2', more: undefined })],
    })]);
  });

  it('preserves technical visuals, sources, and suggestions when rebuilding assistant history', async () => {
    const summaries: ConversationSummary[] = [
      { id: 'conversation', title: 'One', createdAt: 1, lastActivityAt: 1, machineId: 'machine-a', machineName: 'Machine A' },
    ];
    mocks.state = makeState('company_admin', 'machine-a', summaries);
    const createdAtUtc = '2026-10-05T14:07:57Z';
    mocks.getConversationMessages.mockResolvedValue([{ role: 'assistant', content: 'Réponse', createdAtUtc, visuals: [
      { id: 12, documentId: 'manual', page: 71, assetType: 'tile', tile: 'r02-c01', name: 'tile.png', displayOrder: 0 },
    ], sources: [
      { id: 21, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: 0, endIndex: 5, displayOrder: 0 },
    ], suggestions: ['Où est le relais ?'] }]);
    await render();
    await act(async () => host.querySelector<HTMLButtonElement>('[data-testid="conversation-conversation"]')?.click());

    expect(mocks.dispatch).toHaveBeenCalledWith(expect.objectContaining({
      type: 'CHAT_LOAD_CONVERSATION',
      messages: [expect.objectContaining({
        visuals: [expect.objectContaining({ id: 12 })],
        sources: [expect.objectContaining({ id: 21, pdfPage: 74 })],
        suggestions: ['Où est le relais ?'],
        more: { time: createdAtUtc },
      })],
    }));
  });
});
