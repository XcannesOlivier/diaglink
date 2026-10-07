---
name: understanding-architecture
description: Understand the current DiagLink frontend, backend, Claude Direct, document and billing architecture.
---

# DiagLink architecture

React sends authenticated requests to the ASP.NET Core API. `/api/chat/stream` resolves the user,
machine, entitlement, credit and SQL context before invoking Claude Sonnet directly.

```text
Claude Sonnet direct
  +-- Toolbox MCP -> File Search -> machine Vector Store
  +-- get_page_image -> Azure Blob -> full.png / tiles
```

SQL owns users, machines, conversations, messages, summaries, visual history, usage, quota and
wallet state. `ConversationPublicId` is the stable external conversation identifier. Machine PDFs
are ingested independently of ephemeral PNG/JPEG chat attachments.

Important code:

- `backend/WebApp.Api/Program.cs`
- `backend/WebApp.Api/Services/ClaudeDirectChatService.cs`
- `backend/WebApp.Api/Services/ClaudeDirectMachineConfigurationResolver.cs`
- `backend/WebApp.Api/Services/ConversationContextBuilder.cs`
- `backend/WebApp.Api/Repositories/ConversationHistoryRepository.cs`
- `frontend/src/components/AgentChat.tsx`
- `frontend/src/services/chatService.ts`

The Foundry project, Toolbox and Vector Stores remain active infrastructure. Authentication uses
Entra ID, and deployment uses Azure Container Apps plus managed identity/RBAC.
