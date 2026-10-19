import { useRef, useEffect, useLayoutEffect, useState, useDeferredValue, useCallback, type ReactNode } from "react";
import { AssistantMessage } from "./chat/AssistantMessage";
import { UserMessage } from "./chat/UserMessage";
import { StarterMessages } from "./chat/StarterMessages";
import { ChatInput } from "./chat/ChatInput";
import { DropZone } from "./chat/DropZone";
import { Waves } from "./animations/Waves";
import { ErrorMessage } from "./core/ErrorMessage";
import { BuiltWithBadge } from "./core/BuiltWithBadge";
import { DiagLinkLogo } from './core/DiagLinkLogo';
import { Button } from '@fluentui/react-components';
import { Wrench20Regular } from '@fluentui/react-icons';
import type { SelectedMachine } from '../types/machine';
import type { IChatItem } from "../types/chat";
import type { AppState } from "../types/appState";
import type { AppError } from "../types/errors";
import styles from './ChatInterface.module.css';

const ANCHOR_MARGIN_PX = 16;
const FOLLOW_THRESHOLD_PX = 48;

interface ChatInterfaceProps {
  messages: IChatItem[];
  status: AppState['chat']['status'];
  error: AppError | null;
  streamingMessageId?: string;
  recoveredInput?: string;
  recoveredAttachments?: import('../types/chat').IFileAttachment[];
  pendingMessages?: Array<{ text: string; files?: File[] }>;
  onSendMessage: (text: string, files?: File[]) => void;
  onClearError?: () => void;
  onRecoveredInputConsumed?: () => void;
  onDequeueMessage?: (index: number) => void;
  onNewChat?: () => void;
  onCancelStream?: () => void;
  onToggleSidebar?: () => void;
  onOpenMobileMenu?: () => void;
  onRegenerate?: () => void;
  onCancelEdit?: () => void;
  isEditing?: boolean;
  onFeedback?: (messageId: string, rating: 'positive' | 'negative') => void;
  onLoadTechnicalVisual?: (visualId: number, signal?: AbortSignal) => Promise<Blob>;
  onLoadTechnicalSource?: (sourceReferenceId: number, signal?: AbortSignal) => Promise<Blob>;
  hasMessages?: boolean;
  disabled: boolean;
  agentName?: string;
  agentDescription?: string;
  starterPrompts?: string[];
  starterAccessory?: ReactNode;
  onChangeMachine?: () => void;
  machineId?: string;
  selectedMachine?: SelectedMachine | null;
  showMachineSelectionPrompt?: boolean;
}

export const ChatInterface: React.FC<ChatInterfaceProps> = (props) => {
  const { messages, status, error, streamingMessageId, recoveredInput, recoveredAttachments, pendingMessages, onSendMessage, onClearError, onRecoveredInputConsumed, onDequeueMessage, onNewChat, onCancelStream, onToggleSidebar, onOpenMobileMenu, onRegenerate, onCancelEdit, isEditing, onFeedback, onLoadTechnicalVisual, onLoadTechnicalSource, hasMessages, disabled, agentName, agentDescription, starterAccessory, onChangeMachine } = props;
  const deferredMessages = useDeferredValue(messages);
  const messagesContainerRef = useRef<HTMLDivElement>(null);
  const messagesWrapperRef = useRef<HTMLDivElement>(null);
  const anchorSpacerRef = useRef<HTMLDivElement>(null);
  const anchoredMessageIdRef = useRef<string | null>(null);
  const autoFollowRef = useRef(true);
  const renderedMessageIdsRef = useRef<Set<string>>(new Set());
  const [liveRegionMessage, setLiveRegionMessage] = useState<string>('');
  const [isNearBottom, setIsNearBottom] = useState(true);
  const [hasNewMessages, setHasNewMessages] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [droppedFiles, setDroppedFiles] = useState<File[] | undefined>();
  const dragCounterRef = useRef(0);
  
  const isStreaming = status === 'streaming';
  const isBusy = disabled || status === 'sending';
  const isAwaitingResponse = status === 'sending' || isStreaming;

  const findAnchoredMessage = useCallback((): HTMLElement | null => {
    const id = anchoredMessageIdRef.current;
    const container = messagesContainerRef.current;
    if (!id || !container) return null;
    return Array.from(container.querySelectorAll<HTMLElement>('[data-message-id]'))
      .find(el => el.dataset.messageId === id) ?? null;
  }, []);

  // Reserves just enough room below the anchored question so it can sit at the top
  // of the chat area; shrinks to zero as the answer grows, so the scroll height stays stable.
  const updateAnchorSpacer = useCallback(() => {
    const container = messagesContainerRef.current;
    const wrapper = messagesWrapperRef.current;
    const spacer = anchorSpacerRef.current;
    if (!container || !wrapper || !spacer) return;
    const anchor = findAnchoredMessage();
    let height = 0;
    if (anchor) {
      const paddingBottom = parseFloat(getComputedStyle(container).paddingBottom) || 0;
      const contentBelowAnchor = wrapper.getBoundingClientRect().bottom - anchor.getBoundingClientRect().top;
      height = Math.max(0, Math.ceil(container.clientHeight - contentBelowAnchor - ANCHOR_MARGIN_PX - paddingBottom));
    }
    const next = `${height}px`;
    if (spacer.style.height !== next) spacer.style.height = next;
  }, [findAnchoredMessage]);

  const scrollToAnchor = useCallback(() => {
    const container = messagesContainerRef.current;
    const anchor = findAnchoredMessage();
    if (!container || !anchor) return;
    const offset = anchor.getBoundingClientRect().top - container.getBoundingClientRect().top;
    container.scrollTop = Math.max(0, container.scrollTop + offset - ANCHOR_MARGIN_PX);
  }, [findAnchoredMessage]);

  const followContent = useCallback(() => {
    const container = messagesContainerRef.current;
    if (!container || !autoFollowRef.current) return;
    const maxScrollTop = container.scrollHeight - container.clientHeight;
    if (maxScrollTop - container.scrollTop > 1) container.scrollTop = maxScrollTop;
  }, []);

  const scrollToBottom = useCallback(() => {
    const container = messagesContainerRef.current;
    if (!container) return;
    autoFollowRef.current = true;
    container.scrollTo?.({ top: container.scrollHeight, behavior: 'smooth' });
  }, []);

  const handleMessagesScroll = useCallback(() => {
    const container = messagesContainerRef.current;
    if (!container) return;
    const distanceFromBottom = container.scrollHeight - container.scrollTop - container.clientHeight;
    const nearBottom = distanceFromBottom <= FOLLOW_THRESHOLD_PX;
    autoFollowRef.current = nearBottom;
    setIsNearBottom(nearBottom);
    if (nearBottom) setHasNewMessages(false);
  }, []);

  const handleDroppedFilesConsumed = useCallback(() => setDroppedFiles(undefined), []);

  // Runs after the deferred messages are committed to the DOM.
  useLayoutEffect(() => {
    const previousIds = renderedMessageIdsRef.current;
    renderedMessageIdsRef.current = new Set(deferredMessages.map(m => m.id));
    const conversationReplaced = deferredMessages.length === 0
      || (previousIds.size > 0 && !deferredMessages.some(m => previousIds.has(m.id)));
    if (conversationReplaced) {
      anchoredMessageIdRef.current = null;
      autoFollowRef.current = true;
    }

    const lastUserMessage = [...deferredMessages].reverse().find(m => m.role === 'user');
    if (isAwaitingResponse && lastUserMessage && !previousIds.has(lastUserMessage.id)) {
      anchoredMessageIdRef.current = lastUserMessage.id;
      autoFollowRef.current = true;
      updateAnchorSpacer();
      scrollToAnchor();
      setIsNearBottom(true);
      setHasNewMessages(false);
      return;
    }

    updateAnchorSpacer();
    followContent();
  }, [deferredMessages, isAwaitingResponse, updateAnchorSpacer, scrollToAnchor, followContent]);

  // Content can grow without a message change (search steps, images, markdown) and the
  // viewport can resize (mobile keyboard, window resize).
  useEffect(() => {
    const container = messagesContainerRef.current;
    const wrapper = messagesWrapperRef.current;
    if (!container || !wrapper || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => {
      updateAnchorSpacer();
      followContent();
    });
    observer.observe(container);
    observer.observe(wrapper);
    return () => observer.disconnect();
  }, [updateAnchorSpacer, followContent]);

  useEffect(() => {
    if (autoFollowRef.current) {
      setHasNewMessages(false);
    } else if (messages.length > 0) {
      setHasNewMessages(true);
    }
  }, [messages]);

  useEffect(() => {
    if (isStreaming) {
      const streamingMessage = messages.find(m => m.id === streamingMessageId);
      if (streamingMessage?.retryAttempt) {
        setLiveRegionMessage(`Nouvelle tentative, ${streamingMessage.retryAttempt} sur ${streamingMessage.maxRetries}`);
      } else {
        setLiveRegionMessage("L'assistant répond");
      }
    } else if (status === 'idle' && messages.length > 0 && messages[messages.length - 1].role === 'assistant') {
      setLiveRegionMessage('Réponse terminée');
      const timer = setTimeout(() => setLiveRegionMessage(''), 1000);
      return () => clearTimeout(timer);
    }
  }, [isStreaming, status, messages, streamingMessageId]);

  const handleSendMessage = (messageText: string, files?: File[]) => {
    if (!messageText.trim() || disabled) return;
    onSendMessage(messageText, files);
  };

  const handleStarterPromptClick = (prompt: string) => {
    handleSendMessage(prompt);
  };

  // Drag-drop handlers
  const handleDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current++;
    if (e.dataTransfer.types.includes('Files')) {
      setIsDragging(true);
    }
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current--;
    if (dragCounterRef.current === 0) {
      setIsDragging(false);
    }
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current = 0;
    setIsDragging(false);

    const files = Array.from(e.dataTransfer.files);
    if (files.length > 0) {
      setDroppedFiles(files);
    }
  }, []);

  // Global keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      // Ctrl/Cmd+N → new chat
      if (e.key === 'n' && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        onNewChat?.();
      }
    };

    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onNewChat]);

  return (
    <div
      className={styles.chatContainer}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      onDragOver={handleDragOver}
      onDrop={handleDrop}
    >
      <DropZone visible={isDragging} />
      {props.selectedMachine && (
        <section className={styles.machineContext} aria-label="Machine active">
          <div className={styles.machineContextInner}>
            <Wrench20Regular className={styles.machineContextIcon} aria-hidden="true" />
            <div className={styles.machineContextDetails}>
              <span className={styles.machineContextLabel}>Machine active</span>
              <span className={styles.machineContextName}>{props.selectedMachine.name}</span>
              {props.selectedMachine.reference && (
                <span className={styles.machineContextReference}>Réf. {props.selectedMachine.reference}</span>
              )}
            </div>
          </div>
        </section>
      )}
      {/* Live region for announcing streaming status to screen readers */}
      <div 
        role="status" 
        aria-live="polite" 
        aria-atomic="true"
        className="sr-only"
      >
        {liveRegionMessage}
      </div>

      <div 
        ref={messagesContainerRef}
        className={styles.messagesContainer} 
        role="log" 
        aria-live="polite" 
        aria-label="Messages du chat"
        aria-busy={isStreaming}
        onScroll={handleMessagesScroll}
      >
        <div ref={messagesWrapperRef} className={styles.messagesWrapper}>
          {messages.length > 0 && starterAccessory}
          {messages.length === 0 ? (
            <StarterMessages 
              agentName={props.selectedMachine || props.showMachineSelectionPrompt ? 'Assistant Technique' : agentName}
              agentDescription={agentDescription}
              accessory={props.showMachineSelectionPrompt && !props.selectedMachine ? (
                <section className={styles.machineSelectionPrompt} aria-label="Sélection d’une machine">
                  <strong>Choisissez une machine pour commencer</strong>
                  <p>Sélectionnez la machine concernée pour poser vos questions et consulter ses documents.</p>
                  {onChangeMachine && (
                    <Button appearance="primary" onClick={onChangeMachine}>Choisir une machine</Button>
                  )}
                </section>
              ) : starterAccessory}
            />
          ) : (
            <>
              <div aria-live="polite" aria-atomic="false" className="sr-only">
                {messages.length > 0 && messages[messages.length - 1].role === 'assistant' && 
                  `Assistant: ${messages[messages.length - 1].content.substring(0, 100)}`
                }
              </div>
              {(() => {
                return deferredMessages.map((message) => {
                return message.role === "user" ? (
                  <UserMessage 
                    key={message.id} 
                    message={message}
                  />
                ) : (
                  <AssistantMessage 
                    key={message.id} 
                    message={message} 
                    isStreaming={isStreaming && message.id === streamingMessageId}
                    disabled={isBusy}
                    agentName={agentName}
                    onRegenerate={onRegenerate}
                    onFeedback={onFeedback}
                    onLoadTechnicalVisual={onLoadTechnicalVisual}
                    onLoadTechnicalSource={onLoadTechnicalSource}
                    onSuggestedPromptClick={handleStarterPromptClick}
                  />
                );
              })
              })()}
            </>
          )}
        </div>
        <div ref={anchorSpacerRef} aria-hidden="true" data-scroll-anchor-spacer style={{ flexShrink: 0, height: 0 }} />
        {hasNewMessages && !isNearBottom && (
          <button
            className={styles.newMessagesPill}
            onClick={() => { scrollToBottom(); setHasNewMessages(false); }}
            aria-label="Défiler vers les nouveaux messages"
          >
            ↓ Nouveaux messages
          </button>
        )}
      </div>

      <div className={styles.chatInputArea}>
        {error && (
          <div className={styles.errorWrapper}>
            <ErrorMessage
              message={typeof error.message === 'string' ? error.message : 
                      typeof error === 'string' ? error :
                      error.originalError?.message || 
                      'Une erreur inattendue est survenue. Veuillez réessayer.'}
              intent={error.code === 'AiCreditExhausted' ? 'warning' : 'error'}
              recoverable={error.recoverable}
              onRetry={error.action?.handler}
              onDismiss={onClearError}
              customAction={error.action && error.action.label !== 'Réessayer' ? {
                label: error.action.label,
                handler: error.action.handler
              } : undefined}
            />
          </div>
        )}

        <Waves />
        <ChatInput
          onSubmit={handleSendMessage}
          disabled={isBusy}
          onNewChat={onNewChat}
          onToggleSidebar={onToggleSidebar}
          onOpenMobileMenu={onOpenMobileMenu}
          hasMessages={hasMessages}
          placeholder="Décrivez votre problème ou posez votre question…"
          isStreaming={isStreaming}
          onCancelStream={isStreaming && onCancelStream ? onCancelStream : undefined}
          isEditing={isEditing}
          onCancelEdit={onCancelEdit}
          recoveredInput={recoveredInput}
          recoveredAttachments={recoveredAttachments}
          onRecoveredInputConsumed={onRecoveredInputConsumed}
          pendingMessages={pendingMessages}
          onDequeueMessage={onDequeueMessage}
          droppedFiles={droppedFiles}
          onDroppedFilesConsumed={handleDroppedFilesConsumed}
          onChangeMachine={onChangeMachine}
          machineId={props.machineId}
        />
        <div className={styles.poweredRow}>
          <BuiltWithBadge className={styles.builtWithBadge} />
          <DiagLinkLogo className={styles.diagLinkLogo} />
        </div>
      </div>
    </div>
  );
};
