import type { AccountInfo } from '@azure/msal-browser';
import type { IChatItem, IUsageInfo, IAnnotation, IFileAttachment, TechnicalVisual, TechnicalSourceReference } from './chat';
import type { AppError } from './errors';
import type { CurrentUser } from './currentUser';
import type { AppView } from './navigation';
import type { SelectedMachine } from './machine';

// Re-export types for convenience
export type { IChatItem, IUsageInfo, IAnnotation, IFileAttachment, TechnicalVisual, TechnicalSourceReference, CurrentUser, AppView, SelectedMachine };

export interface ConversationSummary {
  id: string;
  title: string | null;
  createdAt: number;
  lastActivityAt: number;
  machineId?: string | null;
  machineName?: string | null;
}

export interface ConversationMessageInfo {
  role: string;
  content: string;
  createdAtUtc?: string;
  visuals?: TechnicalVisual[];
  sources?: TechnicalSourceReference[];
  suggestions?: string[];
}

/**
 * Central application state structure
 * All application state flows through this single source of truth
 */
export interface AppState {
  // Authentication state
  auth: {
    status: 'initializing' | 'authenticated' | 'unauthenticated' | 'error';
    user: AccountInfo | null;
    error: string | null;
    // DiagLink identity (role/companyId), resolved server-side via GET /api/auth/me — populated for
    // both the Microsoft and the DiagLink OTP authentication paths, independently of `status`/`user` above.
    currentUser: CurrentUser | null;
  };

  // Runtime-only company assets. Object URLs stay separate from the server-owned CurrentUser DTO.
  branding: {
    companyId: string | null;
    logoVersion: string | null;
    logoObjectUrl: string | null;
  };
  
  // Chat operations state
  chat: {
    status: 'idle' | 'sending' | 'streaming' | 'error';
    messages: IChatItem[];
    currentConversationId: string | null;
    error: AppError | null;
    streamingMessageId?: string;
    recoveredInput?: string;
    recoveredAttachments?: IFileAttachment[];
    editSnapshot?: IChatItem[]; // messages removed during edit, for undo
    regenerateText?: string;// auto-resend text for regenerate/edit flows
    pendingMessages: Array<{ text: string; files?: File[] }>;
  };

  // Conversation history state
  conversations: {
    list: ConversationSummary[];
    isLoading: boolean;
    sidebarOpen: boolean;
    hasMore: boolean;
  };
  
  // UI coordination state
  ui: {
    chatInputEnabled: boolean; // Disable during streaming/errors
    // Role-gated navigation destination — see utils/navigation.ts for allowed views per role.
    currentView: AppView;
  };

  // Machine currently scoping the active/next conversation (see MachinesView -> AgentChat wiring)
  machine: {
    selected: SelectedMachine | null;
  };
}

/**
 * All possible actions that can modify application state
 * Use discriminated unions for type safety
 */
export type AppAction = 
  // Auth actions
  | { type: 'AUTH_INITIALIZED'; user: AccountInfo }
  | { type: 'AUTH_TOKEN_EXPIRED' }
  | { type: 'AUTH_CURRENT_USER_LOADED'; currentUser: CurrentUser }
  | { type: 'AUTH_CURRENT_USER_CLEARED' }
  | { type: 'COMPANY_LOGO_LOADED'; companyId: string; logoVersion: string | null; logoObjectUrl: string }
  | { type: 'COMPANY_LOGO_CLEARED' }

  // Navigation actions
  | { type: 'UI_SET_VIEW'; view: AppView }
  
  // Chat actions
  | { type: 'CHAT_SEND_MESSAGE'; message: IChatItem }
  | { type: 'CHAT_LOAD_MESSAGES'; messages: IChatItem[] }
  | { type: 'CHAT_START_STREAM'; conversationId?: string; messageId: string }
  | { type: 'CHAT_STREAM_CHUNK'; messageId: string; content: string }
  | { type: 'CHAT_STREAM_ANNOTATIONS'; messageId: string; annotations: IAnnotation[] }
  | { type: 'CHAT_STREAM_VISUALS'; messageId: string; visuals: TechnicalVisual[] }
  | { type: 'CHAT_STREAM_SOURCES'; messageId: string; sources: TechnicalSourceReference[] }
  | { type: 'CHAT_STREAM_SUGGESTIONS'; messageId: string; suggestions: string[] }
  | { type: 'CHAT_STREAM_TOOL_USE'; messageId: string; toolName: string }
  | { type: 'CHAT_STREAM_USAGE'; messageId: string; usage: IUsageInfo }
  | { type: 'CHAT_STREAM_COMPLETE'; messageId: string }
  | { type: 'CHAT_CANCEL_STREAM' }
  | { type: 'CHAT_ERROR'; error: AppError } // Enhanced error object
  | { type: 'CHAT_CLEAR_ERROR' } // Clear error state
  | { type: 'CHAT_CLEAR' }
  | { type: 'CHAT_ADD_ASSISTANT_MESSAGE'; messageId: string }
  | { type: 'CHAT_LOAD_CONVERSATION'; conversationId: string; messages: IChatItem[] }
  | { type: 'CHAT_STREAM_RETRY'; messageId: string; attempt: number; maxRetries: number }
  | { type: 'CHAT_RECOVER_MESSAGE'; messageText: string; error: AppError; retryCount: number }
  | { type: 'CHAT_CONSUMED_RECOVERED_INPUT' }
  | { type: 'CHAT_QUEUE_MESSAGE'; text: string; files?: File[] }
  | { type: 'CHAT_DEQUEUE_MESSAGE'; index: number }
  | { type: 'CHAT_CLEAR_QUEUE' }
  | { type: 'CHAT_REGENERATE' }
  | { type: 'CHAT_EDIT_MESSAGE'; messageId: string; newText: string }
  | { type: 'CHAT_CANCEL_EDIT' }
  | { type: 'CHAT_CONSUMED_REGENERATE' }

  // Conversation history actions
  | { type: 'CONVERSATIONS_SET_LIST'; conversations: ConversationSummary[]; hasMore: boolean; append?: boolean }
  | { type: 'CONVERSATIONS_LOADING' }
  | { type: 'CONVERSATIONS_LOADING_DONE' }
  | { type: 'CONVERSATIONS_TOGGLE_SIDEBAR' }
  | { type: 'CONVERSATIONS_REMOVE'; conversationId: string }
  | { type: 'CONVERSATIONS_COLLAPSE'; keepCount: number }

  // Machine selection actions
  | { type: 'MACHINE_SELECT'; machine: SelectedMachine }
  | { type: 'MACHINE_CLEAR' };

/**
 * Initial state for the application
 */
export const initialAppState: AppState = {
  auth: {
    status: 'initializing',
    user: null,
    error: null,
    currentUser: null,
  },
  branding: {
    companyId: null,
    logoVersion: null,
    logoObjectUrl: null,
  },
  chat: {
    status: 'idle',
    messages: [],
    currentConversationId: null,
    error: null,
    streamingMessageId: undefined,
    recoveredInput: undefined,
    recoveredAttachments: undefined,
    editSnapshot: undefined,
    regenerateText: undefined,
    pendingMessages: [],
  },
  conversations: {
    list: [],
    isLoading: false,
    sidebarOpen: false,
    hasMore: false,
  },
  ui: {
    chatInputEnabled: true,
    currentView: 'chat',
  },
  machine: {
    selected: null,
  },
};
