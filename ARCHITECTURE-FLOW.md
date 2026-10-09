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

## Company identity in the authenticated UI

The company logo uses the existing global session Object URL, with no additional download.
For company administrators and technicians it appears after the desktop role badge and beside
the existing name/role block in the mobile/tablet drawer. Missing or failed images leave no
placeholder; DiagLink branding and super-administrator identity remain unchanged.

Only company administrators can open the separate Personalization view from Settings, not the
primary desktop/mobile navigation. Logo actions apply
immediately using multipart upload or deletion; the preset accent selection remains local until
saved. A confirmed full reset removes both. Each successful write refreshes `/api/auth/me`, leaving
the existing session-logo lifecycle responsible for version changes. If that refresh fails, the
view offers a refresh-only retry rather than repeating the mutation. The preview does not change
Fluent UI theme tokens.

The authenticated `CompanyAccentProvider` derives a Fluent brand ramp from the saved accent.
It overrides only brand-derived tokens, including neutral-named brand interactions and selected
strokes. Public/global themes, neutral surfaces, disabled tokens and semantic palettes are unchanged.
With no accent it supplies no theme override. Fluent portals inherit the same scoped theme.
The scoped provider passes only theme tokens and base typography to portals, not the shell's
layout and opaque background, which would cover the application.
Buttons, links, desktop/mobile selection, form controls, loading indicators and chat accents
(including citations, suggestions, user borders and enabled input focus) use these tokens;
decorative chat waves use the same saved accent. The root custom property remains available.
Dark-mode foregrounds and interaction shades are derived, not stored; normal button fills keep
the saved color. On-brand text uses white or black according to contrast. Foregrounds are adjusted
only when needed for readable text. The six presets support white button text.
Explicit removal/destructive controls keep neutral/red icon interactions; primary destructive
actions use the existing red semantic palette instead of brand fills when branding is active.
Financial status badges retain their original informational colors through scoped aliases,
except the subscription activation badge, which follows the company brand tokens;
quota/wallet fills, warning/success states, active microphone, role badges, document contents
and DiagLink assets keep their independent semantics. The default palette preview uses the base
theme rather than the currently customized one.

## Usage and billing

Claude chat and summary calls persist provider usage before common quota/wallet billing. The
`VisionTool` usage type remains read-only so historical financial rows can still be interpreted;
new Claude Direct requests do not produce it.
