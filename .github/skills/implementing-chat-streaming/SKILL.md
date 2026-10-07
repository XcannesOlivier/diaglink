---
name: implementing-chat-streaming
description: Implement or review DiagLink Claude Direct chat streaming and SSE behavior.
---

# Chat streaming

The only production chat runtime is `IClaudeDirectChatRuntime`.

Request flow:

1. authenticate and resolve the canonical user;
2. resolve machine access and entitlement;
3. reject non-resumable historical conversations;
4. validate credit and PNG/JPEG attachments;
5. rebuild context from SQL;
6. resolve machine `toolbox.json`;
7. call Claude Sonnet directly;
8. stream text/tools/citations/visuals through SSE;
9. persist messages, visuals, summaries and usage.

Preserve Toolbox MCP, File Search, machine Vector Stores and `get_page_image`. Never accept
client-supplied machine AI configuration. Keep provider errors behind the existing SSE error mapping.

Primary files:

- `backend/WebApp.Api/Program.cs`
- `backend/WebApp.Api/Services/ClaudeDirectChatRuntime.cs`
- `backend/WebApp.Api/Services/ClaudeDirectChatService.cs`
- `backend/WebApp.Api/Services/ClaudeDirectMachineConfigurationResolver.cs`
- `backend/WebApp.Api/Repositories/ConversationHistoryRepository.cs`
- `frontend/src/services/chatService.ts`
- `frontend/src/utils/sseParser.ts`
