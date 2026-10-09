import type { Dispatch } from 'react';
import type { AppAction } from '../types/appState';
import type { ConversationSummary, ConversationMessageInfo } from '../types/appState';
import type { IChatItem } from '../types/chat';
import type { AppError } from '../types/errors';
import { isAppError } from '../types/errors';
import { trackException } from './telemetry';
import {
  createAppError,
  getErrorCodeFromMessage,
  parseErrorFromResponse,
  getErrorCodeFromResponse,
  isTokenExpiredError,
} from '../utils/errorHandler';
import {
  convertFilesToDataUris,
  createAttachmentMetadata,
} from '../utils/fileAttachments';
import { parseSseLine, splitSseBuffer } from '../utils/sseParser';
import { getApiAuthHeaders, clearDiagLinkSession } from '../utils/apiAuth';
import { parseTechnicalVisuals } from '../utils/technicalVisuals';
import { parseTechnicalSources } from '../utils/technicalSources';

/**
 * ChatService handles all chat-related API operations.
 * Dispatches AppContext actions for state management.
 *
 * @example
 * ```typescript
 * const chatService = new ChatService(
 *   '/api',
 *   getAccessToken,
 *   dispatch
 * );
 *
 * // Send a message with images
 * await chatService.sendMessage(
 *   'Analyze this image',
 *   currentThreadId,
 *   [imageFile]
 * );
 * ```
 */
export class ChatService {
  private apiUrl: string;
  private getAccessToken: () => Promise<string | null>;
  private dispatch: Dispatch<AppAction>;
  private onDiagLinkSessionExpired?: () => void;
  private onConversationCompleted?: () => void;
  private currentStreamAbort?: AbortController;
  // Flag indicating an intentional user cancellation of the active stream.
  private streamCancelled = false;
  // Which auth mode produced the headers for the most recent request — scopes 401 handling to DiagLink.
  private lastAuthMode: 'diaglink' | 'microsoft' = 'microsoft';

  constructor(
    apiUrl: string,
    getAccessToken: () => Promise<string | null>,
    dispatch: Dispatch<AppAction>,
    onDiagLinkSessionExpired?: () => void,
    onConversationCompleted?: () => void
  ) {
    this.apiUrl = apiUrl;
    this.getAccessToken = getAccessToken;
    this.dispatch = dispatch;
    this.onDiagLinkSessionExpired = onDiagLinkSessionExpired;
    this.onConversationCompleted = onConversationCompleted;
  }

  /**
   * Resolve headers for a protected API call: DiagLink session takes precedence when present
   * in sessionStorage, otherwise falls back to the Microsoft MSAL bearer token as before.
   * @throws {AppError} If neither authentication path yields a usable credential
   */
  private async getAuthHeaders(): Promise<Record<string, string>> {
    try {
      const { headers, mode } = await getApiAuthHeaders(this.getAccessToken);
      this.lastAuthMode = mode;
      return headers;
    } catch (error) {
      throw createAppError(error, 'AUTH');
    }
  }

  /**
   * Ends an invalid DiagLink session on 401: clears sessionStorage and notifies the app so it
   * falls back to the login screen. No-op for Microsoft responses or non-401 statuses.
   */
  private handleUnauthorized(response: Response): void {
    if (response.status === 401 && this.lastAuthMode === 'diaglink') {
      clearDiagLinkSession();
      this.onDiagLinkSessionExpired?.();
    }
  }

  /**
   * Prepare a Claude Direct message payload with optional PNG/JPEG images.
   *
   * @param text - Message text content
   * @param files - Optional array of images
   * @returns Payload with content, image URIs, and attachment metadata
   */
  private async prepareMessagePayload(
    text: string,
    files?: File[]
  ): Promise<{
    content: string;
    imageDataUris: string[];
    attachments: IChatItem['attachments'];
  }> {
    let imageDataUris: string[] = [];
    let attachments: IChatItem['attachments'] = undefined;

    if (files && files.length > 0) {
      try {
        const results = await convertFilesToDataUris(files);

        imageDataUris = results.map((r) => r.dataUri);

        // Create attachment metadata for UI display
        attachments = createAttachmentMetadata(results);
      } catch (error) {
        const appError = createAppError(error);
        this.dispatch({ type: 'CHAT_ERROR', error: appError });
        throw appError;
      }
    }

    return { content: text, imageDataUris, attachments };
  }

  /**
   * Construct request body for chat API.
   *
   * @param message - User message text
   * @param conversationId - Current conversation ID (null for new conversations)
   * @param imageDataUris - Array of base64 data URIs for images
   * @returns Request body object
   */
  private constructRequestBody(
    message: string,
    conversationId: string | null,
    imageDataUris: string[],
    machineId?: string
  ): Record<string, unknown> {
    return {
      message,
      conversationId,
      // Only meaningful (and only ever sent) when starting a new conversation — an existing
      // conversation's machine is re-derived server-side from SQL, never from the client.
      machineId: conversationId ? undefined : machineId,
      imageDataUris: imageDataUris.length > 0 ? imageDataUris : undefined,
    };
  }

  /**
   * Initiate streaming fetch request to chat API.
   * Validates response and throws typed errors on failure.
   *
   * @param url - API endpoint URL
   * @param token - Access token
   * @param body - Request body
   * @param signal - Abort signal for cancellation
   * @returns Response object
   * @throws {AppError} If request fails or response is not OK
   */
  private async initiateStream(
    url: string,
    authHeaders: Record<string, string>,
    body: Record<string, unknown>,
    signal: AbortSignal
  ): Promise<Response> {
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders,
      },
      body: JSON.stringify(body),
      signal,
    });

    if (!res.ok) {
      this.handleUnauthorized(res);
      const errorMessage = await parseErrorFromResponse(res);
      const errorCode = getErrorCodeFromResponse(res);
      const appError = createAppError(new Error(errorMessage), errorCode);
      throw errorCode === 'AiCreditExhausted'
        ? { ...appError, message: errorMessage }
        : appError;
    }

    return res;
  }

  /**
   * Send a message and stream the response from the Azure AI Agent.
   * Orchestrates authentication, file conversion, optimistic UI updates, and streaming.
   * Retries the full stream cycle up to 3 times on retryable errors, then recovers
   * the message text back to the input if all retries fail.
   *
   * @param messageText - The user's message text
   * @param currentConversationId - Current conversation ID (null for new conversations)
   * @param files - Optional array of files to attach (images and documents)
   * @throws {Error} If authentication fails (non-retryable)
   */
  async sendMessage(
    messageText: string,
    currentConversationId: string | null,
    files?: File[],
    machineId?: string
  ): Promise<void> {
    if (this.currentStreamAbort) {
      this.streamCancelled = true;
      this.currentStreamAbort.abort();
      this.dispatch({ type: 'CHAT_CANCEL_STREAM' });
    }

    let authHeaders: Record<string, string>;
    try {
      authHeaders = await this.getAuthHeaders();
    } catch (error) {
      if (isTokenExpiredError(error)) {
        this.dispatch({ type: 'AUTH_TOKEN_EXPIRED' });
      }
      const appError: AppError = isAppError(error)
        ? error
        : createAppError(error, 'AUTH');
      this.dispatch({ type: 'CHAT_ERROR', error: appError });
      throw error;
    }

    const { content, imageDataUris, attachments } = await this.prepareMessagePayload(
      messageText,
      files
    );

    const userMessage: IChatItem = {
      id: Date.now().toString(),
      role: 'user',
      content,
      attachments,
      more: {
        time: new Date().toISOString(),
      },
    };

    this.dispatch({ type: 'CHAT_SEND_MESSAGE', message: userMessage });

    const assistantMessageId = (Date.now() + 1).toString();
    this.dispatch({ type: 'CHAT_ADD_ASSISTANT_MESSAGE', messageId: assistantMessageId });
    this.dispatch({
      type: 'CHAT_START_STREAM',
      conversationId: currentConversationId || undefined,
      messageId: assistantMessageId,
    });

    const requestBody = this.constructRequestBody(
      messageText,
      currentConversationId,
      imageDataUris,
      machineId
    );

    // Each retry can launch another billable provider response after partial consumption.
    // Usage events describe individual attempts, not an idempotent total for this UI message.
    const maxRetries = 3;
    let lastError: unknown;
    let hasReceivedVisibleChunk = false;

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        if (attempt > 1) {
          this.dispatch({
            type: 'CHAT_STREAM_RETRY',
            messageId: assistantMessageId,
            attempt,
            maxRetries,
          });
          await new Promise(resolve => setTimeout(resolve, 1000 * Math.pow(2, attempt - 1)));
        }

        this.currentStreamAbort = new AbortController();
        this.streamCancelled = false;

        const response = await this.initiateStream(
          `${this.apiUrl}/chat/stream`,
          authHeaders,
          requestBody,
          this.currentStreamAbort.signal
        );

        await this.processStream(
          response,
          assistantMessageId,
          currentConversationId,
          () => {
            hasReceivedVisibleChunk = true;
          }
        );
        this.currentStreamAbort = undefined;
        this.streamCancelled = false;
        return;
       } catch (error) {
         lastError = error;
         this.currentStreamAbort = undefined;
         this.streamCancelled = false;

         // User cancelled
         if (error instanceof DOMException && error.name === 'AbortError') {
           return;
         }

         if (isTokenExpiredError(error)) {
           this.dispatch({ type: 'AUTH_TOKEN_EXPIRED' });
           throw error;
         }

         if (isAppError(error) && error.code === 'AiCreditExhausted') {
           this.dispatch({
             type: 'CHAT_RECOVER_MESSAGE',
             messageText,
             error,
             retryCount: 0,
           });
           throw error;
         }

         if (isAppError(error) && error.code === 'AUTH') {
           this.dispatch({ type: 'CHAT_ERROR', error });
           throw error;
         }

         // Never retry a provider request after visible content has already
         // been streamed to the user. A retry could create another billable call.
         if (hasReceivedVisibleChunk) {
           const appError: AppError = isAppError(error)
             ? error
             : createAppError(error, getErrorCodeFromMessage(error));

           this.dispatch({
             type: 'CHAT_ERROR',
             error: appError,
           });

           trackException(
             error instanceof Error ? error : new Error(String(error)),
             {
               context: 'sendMessage-after-partial-stream',
               retryCount: String(attempt - 1),
             }
           );

           return;
         }

        if (attempt === maxRetries) {
          break;
        }
      }

    }

    trackException(lastError instanceof Error ? lastError : new Error(String(lastError)), {
      context: 'sendMessage',
      retryCount: String(maxRetries),
    });

    const appError: AppError = isAppError(lastError)
      ? lastError
      : createAppError(lastError, getErrorCodeFromMessage(lastError));

    this.dispatch({
      type: 'CHAT_RECOVER_MESSAGE',
      messageText,
      error: appError,
      retryCount: maxRetries,
    });
  }

  /**
   * Process Server-Sent Events stream from the API.
   * Implements duplicate chunk suppression to prevent UI flicker.
   *
   * @param response - Fetch Response object with SSE stream
   * @param messageId - ID of the assistant message being streamed
   * @param currentConversationId - Current conversation ID (null for new conversations)
   * @throws {Error} If stream is not readable or parsing fails
   */
  private async processStream(
    response: Response,
    messageId: string,
    currentConversationId: string | null,
    onVisibleChunk?: () => void
  ): Promise<void> {
    const reader = response.body?.getReader();
    const decoder = new TextDecoder();

    if (!reader) {
      const error = createAppError(
        new Error(`Response body is not readable for message ${messageId}`),
        'STREAM'
      );
      this.dispatch({ type: 'CHAT_ERROR', error });
      throw error;
    }

    let newConversationId = currentConversationId;
    let lastChunkContent: string | undefined;
    let buffer = '';

    try {
      while (true) {
        if (this.streamCancelled) {
          break;
        }

        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        buffer += chunk;

        const [lines, remaining] = splitSseBuffer(buffer);
        buffer = remaining;

        for (const line of lines) {
          const event = parseSseLine(line);
          if (!event) continue;

          if (event.data?.error) {
            console.error('[ChatService] SSE error event received:', event.data.error);
            const error = createAppError(
              new Error(event.data.error.message || event.data.error || 'Stream error occurred'),
              'STREAM'
            );
            this.dispatch({ type: 'CHAT_ERROR', error });
            throw error;
          }

          switch (event.type) {
            case 'conversationId':
              if (!newConversationId) {
                newConversationId = event.data.conversationId;
                this.dispatch({
                  type: 'CHAT_START_STREAM',
                  conversationId: event.data.conversationId,
                  messageId,
                });
              }
              break;

            case 'chunk':
              if (event.data.content !== lastChunkContent) {
                if (event.data.content.length > 0) {
                  onVisibleChunk?.();
                }

                this.dispatch({
                  type: 'CHAT_STREAM_CHUNK',
                  messageId,
                  content: event.data.content,
                });

                lastChunkContent = event.data.content;
              }
              break;

            case 'annotations':
              if (event.data.annotations && event.data.annotations.length > 0) {
                this.dispatch({
                  type: 'CHAT_STREAM_ANNOTATIONS',
                  messageId,
                  annotations: event.data.annotations,
                });
              }
              break;

            case 'visuals': {
              const visuals = parseTechnicalVisuals(event.data.visuals);
              if (visuals.length > 0) {
                this.dispatch({ type: 'CHAT_STREAM_VISUALS', messageId, visuals });
              }
              break;
            }

            case 'sources':
              this.dispatch({
                type: 'CHAT_STREAM_SOURCES',
                messageId,
                sources: event.data.sources,
              });
              break;

            case 'suggestions':
              if (Array.isArray(event.data.suggestions)) {
                this.dispatch({
                  type: 'CHAT_STREAM_SUGGESTIONS',
                  messageId,
                  suggestions: event.data.suggestions.filter(
                    (value: unknown): value is string => typeof value === 'string'
                  ),
                });
              }
              break;

            case 'toolUse':
              if (event.data.toolName) {
                this.dispatch({
                  type: 'CHAT_STREAM_TOOL_USE',
                  messageId,
                  toolName: event.data.toolName,
                });
              }
              break;

            case 'usage':
              this.dispatch({
                type: 'CHAT_STREAM_USAGE',
                messageId,
                usage: {
                  promptTokens: event.data.promptTokens,
                  completionTokens: event.data.completionTokens,
                  totalTokens: event.data.totalTokens,
                  available: event.data.available,
                  completed: event.data.completed,
                  model: event.data.model,
                  modelSource: event.data.modelSource,
                  duration: event.data.duration,
                },
              });
              break;

            case 'done':
              this.dispatch({ type: 'CHAT_STREAM_COMPLETE', messageId });
              this.onConversationCompleted?.();
              return;

            case 'error': {
              const error = createAppError(
                new Error(`Stream error for message ${messageId}: ${event.data.message}`),
                'STREAM'
              );
              this.dispatch({ type: 'CHAT_ERROR', error });
              throw error;
            }
          }
        }
      }

      if (!this.streamCancelled) {
        throw createAppError(new Error(`Stream ended before done for message ${messageId}`), 'STREAM');
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError' && this.streamCancelled) {
        // User intentionally cancelled the stream - not an error condition
        return;
      }

      const appError =
        error instanceof Error && 'code' in error
          ? error
          : createAppError(
              new Error(
                `Stream processing failed: ${error instanceof Error ? error.message : String(error)} (Conversation: ${currentConversationId}, Message: ${messageId})`
              ),
              'STREAM'
            );
      this.dispatch({ type: 'CHAT_ERROR', error: appError as AppError });
      throw error;
    } finally {
      try {
        reader.releaseLock();
      } catch {
        // Reader may already be released
      }
    }
  }

  /**
   * Clear chat history and reset to empty state.
   * Dispatches CHAT_CLEAR action to remove all messages and conversation ID.
   */
  clearChat(): void {
    this.dispatch({ type: 'CHAT_CLEAR' });
  }

  /**
   * Clear current error state without affecting chat history.
   * Dispatches CHAT_CLEAR_ERROR action.
   */
  clearError(): void {
    this.dispatch({ type: 'CHAT_CLEAR_ERROR' });
  }

  /**
   * Cancel the current streaming response if any is active.
   * Abort controller is not cleared immediately to allow processStream
   * to observe the cancellation flag and exit gracefully.
   */
  cancelStream(): void {
    if (this.currentStreamAbort) {
      this.streamCancelled = true;
      this.currentStreamAbort.abort();
      this.dispatch({ type: 'CHAT_CANCEL_STREAM' });
    }
  }

  async getTechnicalVisualBlob(visualId: number, signal?: AbortSignal): Promise<Blob> {
    const authHeaders = await this.getAuthHeaders();
    const response = await fetch(`${this.apiUrl}/chat/visuals/${encodeURIComponent(String(visualId))}`, {
      headers: authHeaders,
      signal,
    });
    if (!response.ok) {
      this.handleUnauthorized(response);
      throw createAppError(new Error(`Technical visual request failed: ${response.status}`), 'API');
    }
    return response.blob();
  }

  async getTechnicalSourcePdfBlob(sourceReferenceId: number, signal?: AbortSignal): Promise<Blob> {
    try {
      const authHeaders = await this.getAuthHeaders();
      const response = await fetch(
        `${this.apiUrl}/chat/sources/${encodeURIComponent(String(sourceReferenceId))}/document`,
        { headers: authHeaders, signal },
      );
      if (!response.ok) {
        this.handleUnauthorized(response);
        const message = response.status === 401
          ? 'Votre session a expiré. Reconnectez-vous pour ouvrir cette source.'
          : response.status === 403
            ? 'Vous n’avez plus accès à cette source.'
            : response.status === 404
              ? 'Cette source n’est plus disponible.'
              : 'Le document source n’a pas pu être chargé.';
        const error = createAppError(new Error(message), response.status === 401 ? 'AUTH' : 'API');
        throw { ...error, message };
      }
      return response.blob();
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      if (isAppError(error)) throw error;
      const message = 'Le document source n’a pas pu être chargé.';
      const appError = createAppError(new Error(message), 'NETWORK');
      throw { ...appError, message };
    }
  }

  /**
   * List all conversations from the server.
   * @returns Array of conversation summaries
   */
  async listConversations(limit: number = 20, machineId?: string, signal?: AbortSignal): Promise<{ conversations: ConversationSummary[]; hasMore: boolean }> {
    const authHeaders = await this.getAuthHeaders();
    const query = new URLSearchParams({ limit: String(limit) });
    if (machineId) query.set('machineId', machineId);
    const response = await fetch(`${this.apiUrl}/conversations?${query}`, {
      headers: authHeaders,
      signal,
    });

    if (!response.ok) {
      this.handleUnauthorized(response);
      throw createAppError(new Error(`Failed to list conversations: ${response.status}`), 'API');
    }

    return response.json();
  }

  /**
   * Get messages for a specific conversation.
   * @param conversationId - The conversation ID to fetch messages for
   * @returns Array of conversation messages
   */
  async getConversationMessages(conversationId: string, signal?: AbortSignal): Promise<ConversationMessageInfo[]> {
    const authHeaders = await this.getAuthHeaders();
    const response = await fetch(`${this.apiUrl}/conversations/${conversationId}/messages`, {
      headers: authHeaders,
      signal,
    });

    if (!response.ok) {
      this.handleUnauthorized(response);
      throw createAppError(new Error(`Failed to get conversation messages: ${response.status}`), 'API');
    }

    const messages = await response.json() as Array<Record<string, unknown>>;
    return messages.map(message => {
      const visuals = parseTechnicalVisuals(message.visuals);
      const sources = parseTechnicalSources(message.sources);
      return {
        role: typeof message.role === 'string' ? message.role : '',
        content: typeof message.content === 'string' ? message.content : '',
        ...(typeof message.createdAtUtc === 'string' ? { createdAtUtc: message.createdAtUtc } : {}),
        ...(visuals.length > 0 ? { visuals } : {}),
        sources,
        ...(Array.isArray(message.suggestions)
          ? { suggestions: message.suggestions.filter((value): value is string => typeof value === 'string') }
          : {}),
      };
    });
  }

  /**
   * Delete a conversation.
   * @param conversationId - The conversation ID to delete
   */
  async deleteConversation(conversationId: string): Promise<void> {
    const authHeaders = await this.getAuthHeaders();
    const response = await fetch(`${this.apiUrl}/conversations/${conversationId}`, {
      method: 'DELETE',
      headers: authHeaders,
    });

    if (!response.ok) {
      this.handleUnauthorized(response);
      throw createAppError(new Error(`Failed to delete conversation: ${response.status}`), 'API');
    }
  }

}

