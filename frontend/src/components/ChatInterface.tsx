import { useRef, useEffect, useState, useDeferredValue, useCallback, type ReactNode } from "react";
import { AssistantMessage } from "./chat/AssistantMessage";
import { UserMessage } from "./chat/UserMessage";
import { StarterMessages } from "./chat/StarterMessages";
import { ChatInput } from "./chat/ChatInput";
import { DropZone } from "./chat/DropZone";
import { Waves } from "./animations/Waves";
import { ErrorMessage } from "./core/ErrorMessage";
import { BuiltWithBadge } from "./core/BuiltWithBadge";
import { DiagLinkLogo } from './core/DiagLinkLogo';
import type { IChatItem } from "../types/chat";
import type { AppState } from "../types/appState";
import type { AppError } from "../types/errors";
import styles from './ChatInterface.module.css';

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
}

export const ChatInterface: React.FC<ChatInterfaceProps> = (props) => {
  const { messages, status, error, streamingMessageId, recoveredInput, recoveredAttachments, pendingMessages, onSendMessage, onClearError, onRecoveredInputConsumed, onDequeueMessage, onNewChat, onCancelStream, onToggleSidebar, onOpenMobileMenu, onRegenerate, onCancelEdit, isEditing, onFeedback, onLoadTechnicalVisual, onLoadTechnicalSource, hasMessages, disabled, agentName, agentDescription, starterAccessory, onChangeMachine } = props;
  const deferredMessages = useDeferredValue(messages);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const [liveRegionMessage, setLiveRegionMessage] = useState<string>('');
  const [isNearBottom, setIsNearBottom] = useState(true);
  const [hasNewMessages, setHasNewMessages] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [droppedFiles, setDroppedFiles] = useState<File[] | undefined>();
  const dragCounterRef = useRef(0);
  const observerRef = useRef<IntersectionObserver | null>(null);
  
  const isStreaming = status === 'streaming';
  const isBusy = disabled || status === 'sending';

  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, []);

  const handleDroppedFilesConsumed = useCallback(() => setDroppedFiles(undefined), []);

  // Track whether user is near the bottom via IntersectionObserver
  useEffect(() => {
    const el = messagesEndRef.current;
    if (!el) return;

    observerRef.current = new IntersectionObserver(
      ([entry]) => setIsNearBottom(entry.isIntersecting),
      { threshold: 0.1 }
    );
    observerRef.current.observe(el);

    return () => observerRef.current?.disconnect();
  }, []);

  useEffect(() => {
    if (isNearBottom) {
      scrollToBottom();
      setHasNewMessages(false);
    } else if (messages.length > 0) {
      setHasNewMessages(true);
    }
  }, [messages, isNearBottom, scrollToBottom]);

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
        className={styles.messagesContainer} 
        role="log" 
        aria-live="polite" 
        aria-label="Messages du chat"
        aria-busy={isStreaming}
      >
        <div className={styles.messagesWrapper}>
          {messages.length > 0 && starterAccessory}
          {messages.length === 0 ? (
            <StarterMessages 
              agentName={agentName}
              agentDescription={agentDescription}
              accessory={starterAccessory}
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
              <div ref={messagesEndRef} style={{ height: '1px' }} />
            </>
          )}
        </div>
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
