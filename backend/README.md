# DiagLink backend

ASP.NET Core backend for authentication, machine access, SQL conversation history, billing,
document ingestion and the Claude Direct technical assistant.

## AI runtime

`POST /api/chat/stream` uses Claude Sonnet directly through `IClaudeDirectChatRuntime`.
Machine-scoped `ProjectEndpoint`, `BlobPrefix`, `VectorStoreId` and `toolbox.json` resolve:

- Toolbox MCP;
- File Search and the machine Vector Store;
- `get_page_image` over Azure Blob `full.png` and tiles.

`GET /api/agent` returns local Claude Direct metadata. There is no runtime selector or fallback.

## Local configuration

Authentication values are generated into `backend/WebApp.Api/.env` by `azd up`:

```dotenv
AzureAd__Instance=https://login.microsoftonline.com/
AzureAd__TenantId=...
AzureAd__ClientId=...
AzureAd__Audience=api://...
```

Application configuration for Claude Direct, SQL, Blob Storage, email and billing is defined in
`WebApp.Api/appsettings*.json` and deployment secrets. Machine AI configuration remains in SQL.

## Development

```powershell
dotnet build backend/WebApp.Api.Tests/WebApp.Api.Tests.csproj --no-restore
backend/WebApp.Api.Tests/bin/Debug/net10.0/WebApp.Api.Tests.exe --output Detailed --no-progress
```

Manual Azure tests are inconclusive by default and require their explicit environment flags.
