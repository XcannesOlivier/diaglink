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
}));

const makeState = (role: Role, machineId = 'machine-a', conversations: ConversationSummary[] = []) => ({
  auth: { status: 'authenticated', user: null, error: null, currentUser: { userId: 'user', companyId: 'company', role } },
  chat: { status: 'idle', messages: [], currentConversationId: null, error: null, pendingMessages: [] },
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
    cancelStream() {}
    clearError() {}
    clearChat() {}
    deleteConversation() { return Promise.resolve(); }
    sendMessage() { return Promise.resolve(); }
    sendMcpApproval() { return Promise.resolve(); }
    downloadFile() { return Promise.resolve(); }
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
vi.mock('../ChatInterface', () => ({ ChatInterface: () => <div /> }));
vi.mock('../ConversationSidebar', () => ({
  ConversationSidebar: ({ conversations, onSelectConversation }: {
    conversations: ConversationSummary[];
    onSelectConversation: (conversationId: string) => void;
  }) => <div>{conversations.map(conversation => (
    <button type="button" key={conversation.id} onClick={() => onSelectConversation(conversation.id)}>{conversation.id}</button>
  ))}</div>,
}));

describe('AgentChat machine-scoped conversation history', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    sessionStorage.setItem('diaglink_machine_entry_choice_handled', 'true');
    mocks.dispatch.mockClear();
    mocks.listConversations.mockReset().mockResolvedValue({ conversations: [], hasMore: false });
    mocks.getConversationMessages.mockReset().mockResolvedValue([]);
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
      conversations: [{ id: 'conversation-b', title: 'B', createdAt: 2, machineId: 'machine-b', machineName: 'Machine B' }],
      hasMore: false,
    }));
    await act(async () => responseA.resolve({
      conversations: [{ id: 'conversation-a', title: 'A', createdAt: 1, machineId: 'machine-a', machineName: 'Machine A' }],
      hasMore: false,
    }));

    const populatedLists = mocks.dispatch.mock.calls
      .map(call => call[0])
      .filter(action => action.type === 'CONVERSATIONS_SET_LIST' && action.conversations.length > 0);
    expect(populatedLists).toEqual([expect.objectContaining({ conversations: [expect.objectContaining({ id: 'conversation-b' })] })]);
  });

  it('ignores messages from an earlier conversation click that finishes later', async () => {
    const conversation1 = deferred<ConversationMessageInfo[]>();
    const conversation2 = deferred<ConversationMessageInfo[]>();
    const summaries: ConversationSummary[] = [
      { id: 'conversation-1', title: 'One', createdAt: 1, machineId: 'machine-a', machineName: 'Machine A' },
      { id: 'conversation-2', title: 'Two', createdAt: 2, machineId: 'machine-a', machineName: 'Machine A' },
    ];
    mocks.state = makeState('diaglink_super_admin', 'machine-a', summaries);
    mocks.getConversationMessages.mockImplementation((conversationId: string) =>
      conversationId === 'conversation-1' ? conversation1.promise : conversation2.promise);
    await render();

    const buttons = host.querySelectorAll('button');
    await act(async () => buttons[0].click());
    const firstSignal = mocks.getConversationMessages.mock.calls[0][1] as AbortSignal;
    await act(async () => buttons[1].click());
    expect(firstSignal.aborted).toBe(true);

    await act(async () => conversation2.resolve([{ role: 'assistant', content: 'Réponse 2' }]));
    await act(async () => conversation1.resolve([{ role: 'assistant', content: 'Réponse 1' }]));

    const loaded = mocks.dispatch.mock.calls.map(call => call[0]).filter(action => action.type === 'CHAT_LOAD_CONVERSATION');
    expect(loaded).toEqual([expect.objectContaining({ conversationId: 'conversation-2' })]);
  });

  it('preserves technical visuals when rebuilding assistant history', async () => {
    const summaries: ConversationSummary[] = [
      { id: 'conversation', title: 'One', createdAt: 1, machineId: 'machine-a', machineName: 'Machine A' },
    ];
    mocks.state = makeState('company_admin', 'machine-a', summaries);
    mocks.getConversationMessages.mockResolvedValue([{ role: 'assistant', content: 'Réponse', visuals: [
      { id: 12, documentId: 'manual', page: 71, assetType: 'tile', tile: 'r02-c01', name: 'tile.png', displayOrder: 0 },
    ] }]);
    await render();
    await act(async () => host.querySelector<HTMLButtonElement>('button')?.click());

    expect(mocks.dispatch).toHaveBeenCalledWith(expect.objectContaining({
      type: 'CHAT_LOAD_CONVERSATION',
      messages: [expect.objectContaining({ visuals: [expect.objectContaining({ id: 12 })] })],
    }));
  });
});
