import React, { useMemo, useCallback, useEffect, useRef, useState } from 'react';
import { Button, Toaster, Toast, ToastTitle, useId, useToastController } from '@fluentui/react-components';
// logoDiagLink removed from header; branding moved to chat footer
import { ChatInterface } from './ChatInterface';
import { ConversationSidebar } from './ConversationSidebar';
import { useAppState } from '../hooks/useAppState';
import { useAuth } from '../hooks/useAuth';
import { ChatService } from '../services/chatService';
import { getMachines } from '../services/machineService';
import type { MachineDto } from '../types/machine';
import { useAppContext } from '../contexts/AppContext';
import { trackFeedback } from '../services/telemetry';
import { isSuperAdmin, isCompanyAdmin } from '../utils/roles';
import {isMachineEntryChoiceHandled,markMachineEntryChoiceHandled} from '../utils/apiAuth';
import type { IChatItem } from '../types/chat';
import styles from './AgentChat.module.css';

interface AgentChatProps {
  agentId: string;
  agentName: string;
  agentDescription?: string;
  agentLogo?: string;
  starterPrompts?: string[];
  // Called when a protected request using an active DiagLink session gets rejected (401) —
  // lets the app fall back to the login screen. No-op for the Microsoft/MSAL path.
  onDiagLinkSessionExpired?: () => void;
  // Set when the user picks the "Historique" nav entry — opens the existing conversation
  // sidebar once instead of introducing a separate history screen.
  autoOpenHistory?: boolean;
  onOpenMobileMenu?: () => void;
}

// Number of conversations fetched initially and per "load more" / "show less" step.
const CONVERSATIONS_PAGE_SIZE = 5;

export const AgentChat: React.FC<AgentChatProps> = ({ agentName, agentDescription, agentLogo, starterPrompts, onDiagLinkSessionExpired, autoOpenHistory, onOpenMobileMenu }) => {
  const { chat, state } = useAppState();
  const { dispatch } = useAppContext();
  const { getAccessToken } = useAuth();
  const machineToasterId = useId('machine-toaster');
  const { dispatchToast: dispatchMachineToast } = useToastController(machineToasterId);
  const conversationListRequest = useRef<{ generation: number; controller: AbortController } | null>(null);
  const conversationMessagesRequest = useRef<{ generation: number; controller: AbortController } | null>(null);
  const selectedMachineId = state.machine.selected?.id;

  // Create service instances
  const apiUrl = import.meta.env.VITE_API_URL || '/api';
  
  const chatService = useMemo(() => {
    return new ChatService(apiUrl, getAccessToken, dispatch, onDiagLinkSessionExpired);
  }, [apiUrl, getAccessToken, dispatch, onDiagLinkSessionExpired]);

  const handleSendMessage = async (text: string, files?: File[]) => {
    if (chat.status === 'streaming' || chat.status === 'sending') {
      dispatch({ type: 'CHAT_QUEUE_MESSAGE', text, files });
      return;
    }
    // If starting a new conversation, require a selected machine. Offer a toast with machines to choose from.
    if (!chat.currentConversationId && !state.machine.selected) {
      try {
        const result = await getMachines(getAccessToken);
        if (result.kind === 'success' && result.data.length > 0) {
          const machines = result.data as MachineDto[];
          dispatchMachineToast(
            <Toast>
              <ToastTitle>Sélectionnez une machine</ToastTitle>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 8 }}>
                {machines.map(m => (
                  <Button
                    key={m.id}
                    appearance="subtle"
                      onClick={async () => {
                      dispatch({ type: 'MACHINE_SELECT', machine: { id: m.id, name: m.name, reference: m.reference ?? null } });
                      try {
                        await chatService.sendMessage(text, null, files, m.id);
                      } catch {
                        // sendMessage will dispatch errors itself
                      }
                    }}
                  >
                    {m.name}
                  </Button>
                ))}
              </div>
            </Toast>,
            { intent: 'info' }
          );
        } else {
          dispatch({ type: 'CHAT_ERROR', error: { message: 'Aucune machine disponible.', recoverable: false, code: 'API' } });
        }
      } catch {
        dispatch({ type: 'CHAT_ERROR', error: { message: 'Impossible de charger la liste des machines.', recoverable: true, code: 'API' } });
      }
      return;
    }

    await chatService.sendMessage(text, chat.currentConversationId, files, state.machine.selected?.id);
  };

  // Drain the queue when the stream completes
  const pendingRef = useRef(chat.pendingMessages);
  pendingRef.current = chat.pendingMessages;

  useEffect(() => {
    if (chat.status === 'idle' && pendingRef.current.length > 0) {
      const combinedText = pendingRef.current.map(m => m.text).join('\n\n');
      const combinedFiles = pendingRef.current.flatMap(m => m.files || []);
      dispatch({ type: 'CHAT_CLEAR_QUEUE' });
      chatService.sendMessage(
        combinedText,
        chat.currentConversationId,
        combinedFiles.length > 0 ? combinedFiles : undefined
      );
    }
  }, [chat.status, chat.currentConversationId, chatService, dispatch]);

  const handleDequeueMessage = (index: number) => {
    dispatch({ type: 'CHAT_DEQUEUE_MESSAGE', index });
  };

  const handleClearError = () => {
    chatService.clearError();
  };

  const handleNewChat = () => {
    chatService.cancelStream();
    chatService.clearChat();
  };

  const handleCancelStream = () => {
    chatService.cancelStream();
  };

  const handleRecoveredInputConsumed = () => {
    dispatch({ type: 'CHAT_CONSUMED_RECOVERED_INPUT' });
  };

  const handleRegenerate = useCallback(() => {
    chatService.cancelStream();
    dispatch({ type: 'CHAT_REGENERATE' });
  }, [chatService, dispatch]);

  const handleFeedback = useCallback((messageId: string, rating: 'positive' | 'negative') => {
    trackFeedback(messageId, chat.currentConversationId, rating);
  }, [chat.currentConversationId]);

  const handleCancelEdit = useCallback(() => {
    dispatch({ type: 'CHAT_CANCEL_EDIT' });
  }, [dispatch]);

  const handleDownloadFile = useCallback(async (fileId: string, fileName: string, containerId?: string) => {
    try {
      await chatService.downloadFile(fileId, fileName, containerId);
    } catch (err) {
      dispatch({
        type: 'CHAT_ERROR',
        error: { code: 'NETWORK', message: `Échec du téléchargement de ${fileName} : ${err instanceof Error ? err.message : 'Erreur inconnue'}`, recoverable: true },
      });
    }
  }, [chatService, dispatch]);

  const handleLoadTechnicalVisual = useCallback(
    (visualId: number, signal?: AbortSignal) => chatService.getTechnicalVisualBlob(visualId, signal),
    [chatService]
  );

  // Auto-send when regenerateText is set (from regenerate or edit actions)
  useEffect(() => {
    if (chat.regenerateText?.trim() && chat.status === 'idle') {
      const text = chat.regenerateText;
      dispatch({ type: 'CHAT_CONSUMED_REGENERATE' });
      chatService.sendMessage(text, chat.currentConversationId);
    }
  }, [chat.regenerateText, chat.status, chat.currentConversationId, chatService, dispatch]);

  const handleMcpApproval = async (
    approvalRequestId: string,
    approved: boolean,
    previousResponseId: string,
    conversationId: string
  ) => {
    dispatch({ type: 'CHAT_MCP_APPROVAL_RESOLVED', approvalRequestId, resolved: approved ? 'approved' : 'rejected' });
    try {
      await chatService.sendMcpApproval(approvalRequestId, approved, previousResponseId, conversationId);
    } catch {
      // Rollback so user can retry — clears resolved state, restoring buttons
      dispatch({ type: 'CHAT_MCP_APPROVAL_RESOLVED', approvalRequestId, resolved: undefined });
    }
  };

  const loadConversations = useCallback(async (limit: number, appendFrom?: number) => {
    conversationListRequest.current?.controller.abort();
    const request = {
      generation: (conversationListRequest.current?.generation ?? 0) + 1,
      controller: new AbortController(),
    };
    conversationListRequest.current = request;
    dispatch({ type: 'CONVERSATIONS_LOADING' });
    try {
      const result = await chatService.listConversations(limit, selectedMachineId, request.controller.signal);
      if (request.controller.signal.aborted || conversationListRequest.current?.generation !== request.generation) return;
      const conversations = appendFrom === undefined ? result.conversations : result.conversations.slice(appendFrom);
      const hasMore = appendFrom === undefined
        ? result.hasMore
        : conversations.length > 0 && result.hasMore;
      dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations, hasMore, append: appendFrom !== undefined });
    } catch (error) {
      if (request.controller.signal.aborted || conversationListRequest.current?.generation !== request.generation) return;
      dispatch({ type: 'CONVERSATIONS_LOADING_DONE' });
      throw error;
    }
  }, [chatService, dispatch, selectedMachineId]);

  useEffect(() => {
    conversationMessagesRequest.current?.controller.abort();
    if (conversationMessagesRequest.current) conversationMessagesRequest.current.generation++;
    dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations: [], hasMore: false });
    if (selectedMachineId) {
      void loadConversations(CONVERSATIONS_PAGE_SIZE).catch(error => {
        console.error('Failed to load conversations for selected machine:', error);
      });
    } else {
      conversationListRequest.current?.controller.abort();
    }
    return () => {
      conversationListRequest.current?.controller.abort();
      conversationMessagesRequest.current?.controller.abort();
    };
  }, [dispatch, loadConversations, selectedMachineId]);

  const handleToggleSidebar = useCallback(async () => {
    const willOpen = !state.conversations.sidebarOpen;
    dispatch({ type: 'CONVERSATIONS_TOGGLE_SIDEBAR' });
    if (willOpen && selectedMachineId) {
      try {
        await loadConversations(CONVERSATIONS_PAGE_SIZE);
      } catch (error) {
        console.error('Failed to load conversations:', error);
        dispatch({ type: 'CONVERSATIONS_SET_LIST', conversations: [], hasMore: false });
        dispatch({
          type: 'CHAT_ERROR',
          error: {
            code: 'API',
            message: `Impossible de charger l'historique des conversations : ${error instanceof Error ? error.message : 'erreur inconnue'}`,
            recoverable: true,
            action: { label: 'Réessayer', handler: () => handleToggleSidebar() },
          },
        });
      }
    }
  }, [state.conversations.sidebarOpen, selectedMachineId, dispatch, loadConversations]);

  const handleSidebarOpenChange = useCallback((open: boolean) => {
    if (!open && state.conversations.sidebarOpen) {
      dispatch({ type: 'CONVERSATIONS_TOGGLE_SIDEBAR' });
    }
  }, [state.conversations.sidebarOpen, dispatch]);

  // "Historique" nav entry re-uses this same chat surface — just opens the sidebar once.
  useEffect(() => {
    if (autoOpenHistory && !state.conversations.sidebarOpen) {
      void handleToggleSidebar();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoOpenHistory]);

  const handleLoadMoreConversations = useCallback(async () => {
    try {
      const currentCount = state.conversations.list.length;
      await loadConversations(currentCount + CONVERSATIONS_PAGE_SIZE, currentCount);
    } catch (error) {
      console.error('Failed to load more conversations:', error);
    }
  }, [state.conversations.list.length, loadConversations]);

  const handleCollapseConversations = useCallback(() => {
    const keepCount = Math.max(CONVERSATIONS_PAGE_SIZE, state.conversations.list.length - CONVERSATIONS_PAGE_SIZE);
    dispatch({ type: 'CONVERSATIONS_COLLAPSE', keepCount });
  }, [state.conversations.list.length, dispatch]);

  const handleSelectConversation = useCallback(async (conversationId: string) => {
    conversationMessagesRequest.current?.controller.abort();
    const request = {
      generation: (conversationMessagesRequest.current?.generation ?? 0) + 1,
      controller: new AbortController(),
    };
    conversationMessagesRequest.current = request;
    try {
      chatService.cancelStream();
      const messages = await chatService.getConversationMessages(conversationId, request.controller.signal);
      if (request.controller.signal.aborted || conversationMessagesRequest.current?.generation !== request.generation) return;
      const chatItems: IChatItem[] = messages
        .filter(msg => msg.role === 'user' || msg.role === 'assistant')
        .map((msg, index) => ({
          id: `${conversationId}-${index}`,
          role: msg.role as 'user' | 'assistant',
          content: msg.content,
          visuals: msg.role === 'assistant' ? msg.visuals : undefined,
          more: { time: new Date().toISOString() },
        }));

      dispatch({ type: 'CHAT_LOAD_CONVERSATION', conversationId, messages: chatItems });

      // The conversation determines the active machine, not the other way around — resync from the
      // already-fetched conversation list (machineId/machineName come from GET /api/conversations).
      const summary = state.conversations.list.find(c => c.id === conversationId);
      if (summary?.machineId && summary.machineName) {
        dispatch({
          type: 'MACHINE_SELECT',
          machine: { id: summary.machineId, name: summary.machineName, reference: null },
        });
      } else {
        // Legacy conversation with no bound machine — clear any stale selection.
        dispatch({ type: 'MACHINE_CLEAR' });
      }
    } catch (error) {
      if (request.controller.signal.aborted || conversationMessagesRequest.current?.generation !== request.generation) return;
      console.error('Failed to load conversation:', error);
    }
  }, [chatService, dispatch, state.conversations.list]);

  const handleDeleteConversation = useCallback(async (conversationId: string) => {
    // Remove from UI immediately (optimistic)
    dispatch({ type: 'CONVERSATIONS_REMOVE', conversationId });
    if (chat.currentConversationId === conversationId) {
      chatService.clearChat();
    }
    // Attempt server-side delete (may not be supported yet)
    try {
      await chatService.deleteConversation(conversationId);
    } catch (error) {
      // 501 = SDK doesn't support delete yet — item is hidden locally only
      console.warn('Server-side conversation delete not available:', error);
    }
  }, [chatService, dispatch, chat.currentConversationId]);

  // Ce panneau est un choix d’entrée dans l’application, traité une seule fois par session.
  // Son état ne dépend volontairement pas de la machine sélectionnée.
  const [showMachineConfirmation, setShowMachineConfirmation] = useState(()=>!isMachineEntryChoiceHandled());

  const handleMachineEntryChoice = useCallback(() => {
    markMachineEntryChoiceHandled();
    setShowMachineConfirmation(false);
  }, []);

  const handleKeepMachine = handleMachineEntryChoice;

  const handleChangeMachine = useCallback(() => {
    handleMachineEntryChoice();
    dispatch({ type: 'UI_SET_VIEW', view: 'machines' });
  }, [dispatch,handleMachineEntryChoice]);

  const canAccessAdministration = isSuperAdmin(state.auth.currentUser) || isCompanyAdmin(state.auth.currentUser);

  return (
    <div className={styles.content}>
      <Toaster toasterId={machineToasterId} position="top-end" />
      {/* top brand logo removed to avoid duplication with header/footer */}

      <div className={styles.mainContent}>
        <ChatInterface
          starterAccessory={showMachineConfirmation && state.machine.selected ? (
            <div className={styles.machineConfirmationCard} aria-label="Confirmation de la machine sélectionnée">
              <div className={styles.machineConfirmationActions}>
                <Button appearance="primary" onClick={handleKeepMachine}>
                  Garder cette machine
                </Button>
                <Button appearance="secondary" onClick={handleChangeMachine}>
                  Changer de machine
                </Button>
                {canAccessAdministration && (
                  <Button appearance="secondary" onClick={handleKeepMachine}>
                    Administration
                  </Button>
                )}
              </div>
            </div>
          ) : undefined}
          messages={chat.messages}
          status={chat.status}
          error={chat.error}
          streamingMessageId={chat.streamingMessageId}
          recoveredInput={chat.recoveredInput}
          recoveredAttachments={chat.recoveredAttachments}
          onSendMessage={handleSendMessage}
          onClearError={handleClearError}
          onRecoveredInputConsumed={handleRecoveredInputConsumed}
          onNewChat={handleNewChat}
          onCancelStream={handleCancelStream}
          onMcpApproval={handleMcpApproval}
          onToggleSidebar={handleToggleSidebar}
          onOpenMobileMenu={onOpenMobileMenu}
          onRegenerate={handleRegenerate}
          onCancelEdit={handleCancelEdit}
          isEditing={!!chat.editSnapshot}
          onFeedback={handleFeedback}
          onDownloadFile={handleDownloadFile}
          onLoadTechnicalVisual={handleLoadTechnicalVisual}
          conversationId={chat.currentConversationId}
          pendingMessages={chat.pendingMessages}
          onDequeueMessage={handleDequeueMessage}
          hasMessages={chat.messages.length > 0}
          disabled={false}
          agentName={agentName}
          agentDescription={agentDescription}
          agentLogo={agentLogo}
          starterPrompts={starterPrompts}
          onChangeMachine={handleChangeMachine}
          machineId={state.machine.selected?.id}
        />
      </div>

      <ConversationSidebar
        isOpen={state.conversations.sidebarOpen}
        onOpenChange={handleSidebarOpenChange}
        conversations={state.conversations.list}
        isLoading={state.conversations.isLoading}
        hasMore={state.conversations.hasMore}
        currentConversationId={chat.currentConversationId}
        onSelectConversation={handleSelectConversation}
        onNewChat={handleNewChat}
        onDeleteConversation={handleDeleteConversation}
        onLoadMore={handleLoadMoreConversations}
        onCollapse={handleCollapseConversations}
        canCollapse={state.conversations.list.length > CONVERSATIONS_PAGE_SIZE}
      />
      
    </div>
  );
};
