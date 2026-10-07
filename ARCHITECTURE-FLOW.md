# DiagLink architecture

DiagLink uses a single AI runtime: Claude Sonnet through the direct Messages API.

```text
DiagLink Backend
       |
       v
Claude Sonnet direct
       |
       +-- Toolbox MCP
       |       |
       |       v
       |   File Search
       |       |
       |       v
       |   Machine Vector Store
       |
       +-- get_page_image
               |
               v
        Azure Blob Storage
               |
               v
         full.png / tiles
```

## Chat request

`POST /api/chat/stream` performs authentication, machine access and entitlement checks,
credit validation, SQL context reconstruction, machine toolbox resolution, then invokes
`IClaudeDirectChatRuntime`. Text, tool observations, citations, visuals and usage are emitted
over the shared SSE contract. Persisted PDF page references are emitted in a `sources` event
after persistence and before `done`; only their opaque SQL IDs and display coordinates are public.

Machine configuration is server-owned: `ProjectEndpoint`, `BlobPrefix`, `VectorStoreId` and
the toolbox marker select the MCP/File Search resources. Chat attachments are limited to five
PNG/JPEG images of at most 5 MiB each.

## Conversations

SQL is the source of truth for conversation history, summaries, visual references and resolved
source-reference positions.
`ConversationPublicId` is the stable identifier exposed to the frontend. Historical rows without
a `MachineId` remain readable but cannot be resumed.

## Documents and visuals

Machine PDF ingestion populates the machine Vector Store and Blob assets. Claude can call
`get_page_image`; the backend validates the tool request against MCP-visible documents and returns
the matching `full.png` or tile. `ConversationMessageVisuals` preserves displayed visuals in history.

For clickable page references, `{BlobPrefix}/{DocumentId}/page-map.json` is the documentary source
of truth that maps an assistant-visible page label to a physical PDF page and the private source PDF.
`ConversationMessageSourceReferences` preserves the verified mapping with the assistant message.
`GET /api/chat/sources/{sourceReferenceId}/document` re-checks conversation ownership and current
machine access, reloads the page map, then streams the private PDF inline without exposing Blob URLs.

## Usage and billing

Claude chat and summary calls persist provider usage before common quota/wallet billing. The
`VisionTool` usage type remains read-only so historical financial rows can still be interpreted;
new Claude Direct requests do not produce it.
