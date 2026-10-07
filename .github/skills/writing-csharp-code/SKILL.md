---
name: writing-csharp-code
description: Provides C# and ASP.NET Core coding standards for this repository. Use when writing or modifying C# code, implementing API endpoints, configuring middleware, or working with authentication in the backend.
---

# C# Coding Standards

**Goal**: Write clean, secure ASP.NET Core code with proper authentication

## Hot Reload Development Workflow

**The backend runs in watch mode** (`dotnet watch run`). When you edit C# code:

1. **Save the file** - .NET automatically recompiles
2. **Check the terminal** - Look for compilation output in the "Backend: ASP.NET Core API" terminal
3. **Verify via console logs** - New requests will use updated code immediately

**VS Code Tasks** (use `Run Task` command or check terminal panel):
- `Backend: ASP.NET Core API` - Runs `dotnet watch run` with live recompilation
- Logs are visible directly in VS Code terminal

**No restart needed** - Just edit, save, and test. Watch for compilation errors in the terminal.

**Testing changes**: Use Playwright browser tools to make requests and check browser console logs, or call endpoints directly.

## Minimal API Patterns

Use typed request models, CancellationToken, and IHostEnvironment:

```csharp
app.MapPost("/api/endpoint", async (
    RequestModel request,
    MyService service,
    IHostEnvironment env,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ProcessAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return ErrorResponseFactory.CreateFromException(ex, env);
    }
})
.RequireAuthorization("RequireChatScope")
.WithName("EndpointName");
```

## Authentication Setup

**JWT Bearer with Entra ID**:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        options.TokenValidationParameters.ValidAudiences = new[]
        {
            builder.Configuration["AzureAd:ClientId"],
            $"api://{builder.Configuration["AzureAd:ClientId"]}"
        };
    }, options => builder.Configuration.Bind("AzureAd", options));
```

## Async Best Practices

```csharp
// âœ… Use async/await with CancellationToken
public async Task<Result> ProcessAsync(Request req, CancellationToken ct)
{
    return await _service.ExecuteAsync(req, ct);
}

// âŒ Never block on async
var result = _service.ExecuteAsync(req).Result;  // WRONG
```

## IAsyncEnumerable for Streaming

```csharp
public async IAsyncEnumerable<string> StreamAsync(
    string input,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await foreach (var chunk in source.WithCancellation(cancellationToken))
    {
        yield return chunk;
    }
}
```

## Credential Strategy

```csharp
TokenCredential credential = env.IsDevelopment()
    ? new ChainedTokenCredential(
        new AzureCliCredential(),
        new AzureDeveloperCliCredential())  // Supports 'azd auth login'
    : new ManagedIdentityCredential(miClientId); // User-assigned MI in production
```

**Why ChainedTokenCredential**: Avoids `DefaultAzureCredential`'s "fail fast" mode issues. Explicit, predictable credential chain.

## IDisposable Pattern

```csharp
public class MyService : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public void DoWork()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // ...
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Cancel pending operations first
        try { _disposeCts.Cancel(); }
        catch (ObjectDisposedException) { }
        
        _disposeCts.Dispose();
        _lock.Dispose();
    }
}
```

## Error Responses (RFC 7807)

Use `ErrorResponseFactory.CreateFromException()` for consistent error responses.

See: `backend/WebApp.Api/Models/ErrorResponse.cs`

## Common Mistakes

- âŒ Using `.Result` or `.Wait()` on async methods
- âŒ Forgetting `CancellationToken` parameter
- âŒ Missing `.RequireAuthorization()` on endpoints
- âŒ Exposing internal errors in production
- âŒ Forgetting disposal guards in `IDisposable`

---

## Project-Specific: Middleware Pipeline

**Goal**: Serve static files â†’ validate auth â†’ route APIs â†’ SPA fallback

```csharp
app.UseDefaultFiles();     // index.html for /
app.UseStaticFiles();      // wwwroot/* assets  
app.UseCors();             // Dev only
app.UseAuthentication();   // Validate JWT
app.UseAuthorization();    // Enforce scope
// Map endpoints here
app.MapFallbackToFile("index.html");  // MUST BE LAST
```

## Project-Specific: Claude Direct

The production runtime is `IClaudeDirectChatRuntime`. Keep machine configuration server-owned and resolve Toolbox MCP, File Search and `get_page_image` through `ClaudeDirectMachineConfigurationResolver`. Provider HTTP details belong in `ClaudeDirectChatService`; endpoints consume provider-neutral `StreamChunk` values.
## Project-Specific: Configuration Loading

Auto-load `.env` file before building configuration:

```csharp
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile)
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }
}
```

## Troubleshooting AI integration

Inspect `ClaudeDirectChatService`, toolbox marker validation and the provider-neutral request/result models. Never log bearer tokens, MCP secrets, document identifiers or raw tool arguments.
## Related Skills

- **implementing-chat-streaming** - SSE streaming patterns and backend endpoint implementation
- **troubleshooting-authentication** - MSAL/JWT debugging for 401 errors
- **researching-azure-ai-sdk** - SDK research workflow and sample repositories
