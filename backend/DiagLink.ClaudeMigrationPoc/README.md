# Claude / Foundry Toolbox migration POC

This is a deliberately isolated, manually executed console POC. No production
project references it, and it does not expose an HTTP endpoint or persist data.

It proves three paths against the real Foundry resources:

1. a direct Claude Messages API call;
2. Claude using the DX10z Foundry Toolbox through the MCP connector;
3. one orchestration that uses MCP File Search, executes the local
   `get_page_image` tool, reads page 75 from Blob Storage, and sends the PNG as
   an image `tool_result` back to the same Claude deployment.

## Authentication and configuration

Run under an identity accepted by `DefaultAzureCredential` (for example after
`az login`). The same Entra token, scoped to `https://ai.azure.com/.default`, is
used for the Claude request and the MCP server `authorization_token`. Tokens
and request bodies are never logged.

Blob access also uses `DefaultAzureCredential`. The real POC service URI defaults
to `https://stknowledgeia.blob.core.windows.net/`. It can be overridden with:

```powershell
$env:POC_BLOB_SERVICE_URI = 'https://<storage-account>.blob.core.windows.net'
```

If that variable is absent, the POC can derive only the service URI/account
name from `AZURE_STORAGE_CONNECTION_STRING`; it never uses or logs the storage
key. The identity needs Blob Data Reader access. The container defaults to
`documents`; override it only if needed with `POC_BLOB_CONTAINER`.

## Manual execution

From the repository root:

```powershell
dotnet run --project backend/DiagLink.ClaudeMigrationPoc -- all
```

Modes are `direct`, `mcp`, `full`, or `all` (default). `full` is the image tool
loop. Every Messages call prints its model, stop reason, and token usage, then
the process prints global totals. This project is intentionally not part of
ordinary automated test execution.
