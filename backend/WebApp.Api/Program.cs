using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Repositories;
using WebApp.Api.Services;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

// Load .env file for local development BEFORE building the configuration
// In production (Docker), Container Apps injects environment variables directly
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            // Set as environment variables so they're picked up by configuration system
            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Enable PII logging for debugging auth issues (ONLY IN DEVELOPMENT)
if (builder.Environment.IsDevelopment())
{
    Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
}

// Add ServiceDefaults (telemetry, health checks)
builder.AddServiceDefaults();

// Add ProblemDetails service for standardized RFC 7807 error responses
builder.Services.AddProblemDetails();

// Register IHttpContextAccessor for services that need access to the current HTTP request
builder.Services.AddHttpContextAccessor();

// Configure CORS for local development and production
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:8080" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // In development, allow any localhost port for flexibility
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin => 
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                }
                return false;
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Override ClientId and TenantId from environment variables if provided
// These will be set by azd during deployment or by AppHost in local dev
var clientId = builder.Configuration["ENTRA_SPA_CLIENT_ID"]
    ?? builder.Configuration["AzureAd:ClientId"];

if (!string.IsNullOrEmpty(clientId))
{
    builder.Configuration["AzureAd:ClientId"] = clientId;
    // Set audience to match the expected token audience claim
    builder.Configuration["AzureAd:Audience"] = $"api://{clientId}";
}

var tenantId = builder.Configuration["ENTRA_TENANT_ID"]
    ?? builder.Configuration["AzureAd:TenantId"];

if (!string.IsNullOrEmpty(tenantId))
{
    builder.Configuration["AzureAd:TenantId"] = tenantId;
}

const string ScopePolicyName = "RequireChatScope";

// Add Microsoft Identity Web authentication
// Validates JWT bearer tokens issued for the SPA's delegated scope
var authenticationBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
authenticationBuilder.AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        var configuredClientId = builder.Configuration["AzureAd:ClientId"];
        var backendClientId = builder.Configuration["ENTRA_BACKEND_CLIENT_ID"];

        // When OBO is enabled, tokens are scoped to the backend API app
        var audiences = new List<string> { configuredClientId!, $"api://{configuredClientId}" };
        if (!string.IsNullOrEmpty(backendClientId))
        {
            audiences.Add(backendClientId);
            audiences.Add($"api://{backendClientId}");
        }
        options.TokenValidationParameters.ValidAudiences = audiences;

        options.TokenValidationParameters.NameClaimType = ClaimTypes.Name;
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    }, options => builder.Configuration.Bind("AzureAd", options));

// Additional scheme for DiagLink OTP sessions (X-DiagLink-Session header) — registered so it exists
// side-by-side with the Microsoft JWT bearer scheme. Chat endpoints accept either (see ChatAccessRequirement
// below); the canonical UserObjectId for either path is resolved by UserIdentityService, not GetObjectId() directly.
authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DiagLinkSessionAuthenticationHandler>(
    DiagLinkAuthenticationDefaults.Scheme, _ => { });

// Enriches Microsoft JWT principals with the same Role/CompanyId/Email claims the DiagLink session
// handler already attaches — see DiagLinkUserClaimsTransformation for why this is a claims transform.
builder.Services.AddScoped<IClaimsTransformation, DiagLinkUserClaimsTransformation>();

builder.Services.AddAuthorization(options =>
{
    // Dual-auth: a valid DiagLink session OR a Microsoft JWT bearer with Chat.ReadWrite — see
    // ChatAccessRequirement/ChatAccessAuthorizationHandler. Kept under the same policy name so every
    // existing .RequireAuthorization(ScopePolicyName) call site on chat endpoints picks this up automatically.
    options.AddPolicy(ScopePolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, DiagLinkAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new ChatAccessRequirement());
    });

    // Role policies — additive, not yet used by any screen/endpoint besides GET /api/auth/me. Same
    // dual-scheme setup as RequireChatScope so both DiagLink sessions and Microsoft JWTs are eligible;
    // whether a given principal actually carries a role claim depends on DiagLinkUserLookupService
    // finding an active dbo.Users row (see DiagLinkSessionAuthenticationHandler / DiagLinkUserClaimsTransformation).
    void AddRolePolicy(string name, params string[] roles) => options.AddPolicy(name, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, DiagLinkAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(roles);
    });

    AddRolePolicy("TechnicianOrAbove", DiagLinkRoles.Technician, DiagLinkRoles.CompanyAdmin, DiagLinkRoles.SuperAdmin);
    AddRolePolicy("CompanyAdminOrAbove", DiagLinkRoles.CompanyAdmin, DiagLinkRoles.SuperAdmin);
    AddRolePolicy("SuperAdminOnly", DiagLinkRoles.SuperAdmin);

    // Strictly company_admin — excludes diaglink_super_admin on purpose. A super-admin's company_id
    // claim isn't a meaningful tenant, so endpoints scoped by that claim (technician provisioning,
    // machine assignment) must never be reachable by that role; the super-admin equivalents take an
    // explicit companyId route parameter instead (see /api/companies/{companyId}/... endpoints).
    AddRolePolicy("CompanyAdminOnly", DiagLinkRoles.CompanyAdmin);
});

// Register Foundry Agent Service (v2 Agents API)
// Uses Azure.AI.Projects SDK which works with v2 Agents API (/agents/ endpoint with human-readable IDs).
builder.Services.AddHttpClient();
builder.Services.AddScoped<AgentFrameworkService>();
builder.Services.AddScoped<AiPricingIdentityResolver>();

// Conversation-history persistence — isolated 'chat' schema in the shared 'diaglink' Azure SQL database.
builder.Services.AddDbContext<DiagLinkDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DiagLink")
            ?? throw new InvalidOperationException("ConnectionStrings:DiagLink is not configured"),
        sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", DiagLinkDbContext.Schema);
            // Absorbs transient SQL errors (incl. Azure SQL serverless cold-start) without manual retry logic per endpoint.
            sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }));
builder.Services.AddScoped<ConversationHistoryRepository>();
builder.Services.AddScoped<AiUsageRepository>();
builder.Services.AddScoped<AiUsageQueryService>();
builder.Services.AddScoped<AiCostCalculator>();
builder.Services.AddScoped<AiCostCurrencyConverter>();
builder.Services.AddScoped<AiCreditConsumptionService>();
builder.Services.AddScoped<CompanyWalletDebitService>();
builder.Services.AddScoped<MachineBillingPeriodService>();
builder.Services.AddScoped<AiCreditAccessService>();
builder.Services.AddScoped<AiUsageBillingOrchestrator>();
builder.Services.AddScoped<IAiUsageBillingOrchestrator>(sp => sp.GetRequiredService<AiUsageBillingOrchestrator>());
builder.Services.AddScoped<AiUsagePersistenceBillingService>();
// Opt-in Stripe billing; dedicated signed payment webhook, no background calls.
builder.Services.AddSingleton(sp => StripeBillingOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IStripeBillingGateway, StripeBillingGateway>();
builder.Services.AddScoped<IStripeMachineAdditionGateway, StripeBillingGateway>();
builder.Services.AddScoped<StripeBillingService>();
builder.Services.AddScoped<IStripeSubscriptionPaymentGateway, StripeBillingGateway>();
builder.Services.AddScoped<StripeSubscriptionPaymentService>();
builder.Services.AddScoped<StripeSubscriptionWebhook>();
builder.Services.AddScoped<IStripeLifecycleGateway, StripeBillingGateway>();
builder.Services.AddScoped<StripeLifecycleService>();
builder.Services.AddScoped<StripeMachineStatusService>();
builder.Services.AddScoped<StripeMachineAdditionService>();
builder.Services.AddScoped<StripeMachineAdditionWebhook>();
builder.Services.AddScoped<IStripeWalletTopUpGateway, StripeWalletTopUpGateway>();
builder.Services.AddScoped<CompanyWalletTopUpService>();
builder.Services.AddScoped<StripeWalletTopUpWebhook>();
builder.Services.AddScoped<MachineRequestPaymentStore>();
builder.Services.AddScoped<IMachineRequestPaymentGateway, StripeMachineRequestPaymentGateway>();
builder.Services.AddScoped<MachineRequestPaymentService>();
builder.Services.AddScoped<AdditionalMachineRequestDecisionService>();
builder.Services.AddScoped<AdditionalDocumentsRequestDecisionService>();
builder.Services.AddScoped<AdditionalMachinePaymentContextResolver>();
builder.Services.AddScoped<AdditionalDocumentsContextResolver>();
builder.Services.AddScoped<IMachineRequestCustomerLinkGateway, StripeMachineRequestCustomerLinkGateway>();
builder.Services.AddScoped<MachineRequestCustomerLinkService>();
builder.Services.AddScoped<IMachineRequestSubscriptionGateway, StripeMachineRequestSubscriptionGateway>();
builder.Services.AddScoped<MachineRequestSubscriptionService>();
builder.Services.AddScoped<MachineRequestActivationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StripeMachineRequestPaymentWebhook>();
builder.Services.AddScoped<ConversationSummaryService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<ITransactionalEmailSender>(services => services.GetRequiredService<EmailService>());
builder.Services.AddScoped<EmailOutboxStore>();
builder.Services.AddScoped<DiagLinkSessionService>();
builder.Services.AddScoped<DiagLinkUserLookupService>();
builder.Services.AddSingleton<OtpRateLimiter>();
builder.Services.AddSingleton<ContactSubmissionRateLimiter>();
builder.Services.AddScoped<UserIdentityService>();
builder.Services.AddScoped<MachineAccessService>();
builder.Services.AddScoped<MachineAssistantResolutionService>();
builder.Services.AddScoped<CompanyDirectoryService>();
builder.Services.AddScoped<CompanyOnboardingService>();
builder.Services.AddScoped<UserProvisioningService>();
builder.Services.AddScoped<MachineAssignmentService>();
builder.Services.AddSingleton<IPdfPageCounter, PdfPigPageCounter>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ChatAccessAuthorizationHandler>();

    // Register BlobServiceClient if AZURE_STORAGE_CONNECTION_STRING is present
    var storageConn = builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"];
    if (!string.IsNullOrEmpty(storageConn))
    {
        builder.Services.AddSingleton(new Azure.Storage.Blobs.BlobServiceClient(storageConn));
        builder.Services.AddScoped<WebApp.Api.Services.BlobStorageService>();
        builder.Services.AddScoped<WebApp.Api.Services.IMachineRequestBlobClient, WebApp.Api.Services.AzureMachineRequestBlobClient>();
        builder.Services.AddScoped<WebApp.Api.Services.MachineRequestStorageService>();
        builder.Services.AddScoped<WebApp.Api.Services.RequestReceivedNotificationService>();
        builder.Services.AddScoped<WebApp.Api.Services.EmailOutboxProcessor>();
        builder.Services.AddHostedService<WebApp.Api.Services.EmailOutboxWorker>();
    }

    var app = builder.Build();

// Add exception handling middleware for production
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

// Add status code pages for consistent error responses
app.UseStatusCodePages();

// Map health checks
app.MapDefaultEndpoints();

// Serve static files from wwwroot (frontend)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowFrontend");

// Note: HTTPS redirection not needed - Azure Container Apps handles SSL termination at ingress
// The container receives HTTP traffic on port 8080

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapStripeAdminEndpoints();
app.MapStripeWalletTopUps();
app.MapMachineRequestPayments();
app.MapCompanyFinance();
app.MapPublicMachineRequests();
app.MapPublicContact();
app.MapAdditionalMachineRequests();
app.MapAdditionalDocumentsRequests();
app.MapAdminMachineRequests();
app.MapPost("/api/stripe/webhooks/machine-additions", StripeSubscriptionWebhook.HandleHttpAsync)
    .AllowAnonymous();

// Unauthenticated health endpoint for container probes
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }))
.WithName("GetHealth");

// POST /api/files/upload — accepts companyName, machineName and PDF files (multipart/form-data)
app.MapPost("/api/files/upload", async (HttpContext httpContext, DiagLinkDbContext db, CancellationToken cancellationToken) =>
{
    var blobService = httpContext.RequestServices.GetService<WebApp.Api.Services.BlobStorageService>();
    if (blobService is null)
    {
        return Results.StatusCode(501); // Not implemented when blob config missing
    }

    var form = await httpContext.Request.ReadFormAsync(cancellationToken);
    var companyName = form["companyName"].FirstOrDefault();
    var machineName = form["machineName"].FirstOrDefault();
    var files = form.Files;

    if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(machineName))
    {
        return Results.BadRequest(new { error = "companyName and machineName are required in form data" });
    }

    // Ensure company exists and create Machine record if missing
    var normalizedCompany = companyName.Trim();
    var company = await db.Companies.FirstOrDefaultAsync(c => c.Name.Trim().ToLower() == normalizedCompany.ToLower(), cancellationToken);
    if (company is null)
    {
        return Results.BadRequest(new { error = "company not found" });
    }

    var normalizedMachineName = machineName.Trim();
    var existingMachine = await db.Machines.FirstOrDefaultAsync(m => m.CompanyId == company.Id && m.Name == normalizedMachineName, cancellationToken);

    // Local slugify helper (same rules as BlobStorageService)
    static string SlugifyLocal(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var normalized = input.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        var cleaned = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "[^A-Za-z0-9]+", "-").Trim('-');
        return cleaned.ToLowerInvariant();
    }

    var companySlug = SlugifyLocal(normalizedCompany);
    var machineSlug = SlugifyLocal(normalizedMachineName);
    var blobPrefix = $"{companySlug}/{machineSlug}";

    if (existingMachine is null)
    {
        var now = DateTime.UtcNow;
        var newMachine = new WebApp.Api.Models.Entities.Machine
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Name = normalizedMachineName,
            Status = "active",
            BlobPrefix = blobPrefix,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Machines.Add(newMachine);
        await db.SaveChangesAsync(cancellationToken);
        existingMachine = newMachine;
    }
    else if (string.IsNullOrWhiteSpace(existingMachine.BlobPrefix))
    {
        existingMachine.BlobPrefix = blobPrefix;
        existingMachine.UpdatedAtUtc = DateTime.UtcNow;
        db.Machines.Update(existingMachine);
        await db.SaveChangesAsync(cancellationToken);
    }

    try
    {
        var uploaded = await blobService.UploadFilesAsync("documents", companyName, machineName, files, cancellationToken);
        return Results.Created("/api/files/upload", new { uploaded });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.RequireAuthorization("SuperAdminOnly")
.Accepts<IFormFileCollection>("multipart/form-data")
.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MachineRequestUploadLimits.MaxRequestBodyBytes))
.WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute
{
    MultipartBodyLengthLimit = MachineRequestUploadLimits.MaxRequestBodyBytes
});

// Public pre-auth compatibility endpoint. Its response is deliberately identical for every
// non-empty email so callers cannot enumerate active dbo.Users records.
app.MapPost("/api/auth/check-email", (
    CheckEmailRequest request) =>
{
    var normalizedEmail = request.Email?.Trim();
    if (string.IsNullOrEmpty(normalizedEmail))
    {
        return Results.BadRequest();
    }

    return Results.Ok(new CheckEmailResponse { Known = true });
})
.WithName("CheckEmail");

// Public pre-auth endpoint: issues a one-time login code for an active dbo.Users record.
// Intentionally has no .RequireAuthorization() — must be callable before any Microsoft sign-in.
// Response is always the same generic shape whether the email is unknown, inactive, or valid.
app.MapPost("/api/auth/request-code", async (
    RequestCodeRequest request,
    HttpContext httpContext,
    DiagLinkDbContext db,
    IConfiguration configuration,
    EmailService emailService,
    OtpRateLimiter rateLimiter,
    IHostEnvironment environment,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var normalizedEmail = request.Email?.Trim();
        if (string.IsNullOrEmpty(normalizedEmail))
        {
            return Results.BadRequest();
        }

        var rateLimit = rateLimiter.AttemptRequestCode(httpContext.Connection.RemoteIpAddress, normalizedEmail);
        if (!rateLimit.IsAllowed) return OtpRateLimitExceeded(httpContext, rateLimit);

        var user = await db.Users
            .Where(u => u.Email.ToLower() == normalizedEmail.ToLower() && u.Status.ToLower() == "active")
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Results.Ok(new RequestCodeResponse { Success = true });
        }

        // Invalidate prior unused codes first — only one code can ever be usable at a time per user.
        var previousUnusedCodes = await db.LoginCodes
            .Where(c => c.UserId == user.Id && c.UsedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var previousCode in previousUnusedCodes)
        {
            previousCode.UsedAtUtc = now;
        }

        // Cryptographically secure 6-digit code — "D6" preserves any leading zeros.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var otpPepper = configuration["Auth:OtpPepper"]
            ?? throw new InvalidOperationException("Auth:OtpPepper is not configured");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(otpPepper));
        var codeHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(code)));

        var newLoginCode = new LoginCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = codeHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            UsedAtUtc = null
        };
        db.LoginCodes.Add(newLoginCode);

        await db.SaveChangesAsync(cancellationToken);

        // DEV ONLY — never log the plaintext code outside Development.
        if (environment.IsDevelopment())
        {
            logger.LogInformation("DEV ONLY: login code for {Email} is {Code}", normalizedEmail, code);
        }

        try
        {
            await emailService.SendLoginCodeAsync(normalizedEmail, code, cancellationToken);
        }
        catch (Exception ex)
        {
            // Sending failed — the stored code would be unusable (never delivered), so invalidate it
            // rather than leaving an active code the user can never receive.
            db.LoginCodes.Remove(newLoginCode);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "OTP email delivery failed; returning the generic public response.");
        }

        return Results.Ok(new RequestCodeResponse { Success = true });
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex,
            500,
            environment.IsDevelopment());

        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.WithName("RequestLoginCode");

app.MapPost("/api/auth/verify-code", async (
    VerifyCodeRequest request,
    HttpContext httpContext,
    DiagLinkDbContext db,
    IConfiguration configuration,
    OtpRateLimiter rateLimiter,
    CancellationToken cancellationToken) =>
{
    var normalizedEmail = request.Email?.Trim();
    var code = request.Code?.Trim();

    if (string.IsNullOrWhiteSpace(normalizedEmail) ||
        string.IsNullOrWhiteSpace(code) ||
        code.Length != 6 ||
        !code.All(char.IsDigit))
    {
        return Results.Ok(new VerifyCodeResponse { Success = false });
    }

    var rateLimit = rateLimiter.AttemptVerifyCode(httpContext.Connection.RemoteIpAddress, normalizedEmail);
    if (!rateLimit.IsAllowed) return OtpRateLimitExceeded(httpContext, rateLimit);

    var user = await db.Users
        .Where(u =>
            u.Email.ToLower() == normalizedEmail.ToLower() &&
            u.Status.ToLower() == "active")
        .Select(u => new { u.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (user is null)
    {
        return Results.Ok(new VerifyCodeResponse { Success = false });
    }

    var now = DateTime.UtcNow;

    var loginCode = await db.LoginCodes
        .Where(c =>
            c.UserId == user.Id &&
            c.UsedAtUtc == null &&
            c.ExpiresAtUtc > now)
        .OrderByDescending(c => c.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    if (loginCode is null)
    {
        return Results.Ok(new VerifyCodeResponse { Success = false });
    }

    var otpPepper = configuration["Auth:OtpPepper"]
        ?? throw new InvalidOperationException("Auth:OtpPepper is not configured");

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(otpPepper));

    var submittedHash = hmac.ComputeHash(
        Encoding.UTF8.GetBytes(code));

    var storedHash = Convert.FromHexString(loginCode.CodeHash);

    var valid = CryptographicOperations.FixedTimeEquals(
        submittedHash,
        storedHash);

    if (!valid)
    {
        return Results.Ok(new VerifyCodeResponse { Success = false });
    }

    // Session DiagLink 24h : token opaque aléatoire, seul son hash est persisté.
    var sessionTokenBytes = RandomNumberGenerator.GetBytes(32);
    var sessionToken = Convert.ToBase64String(sessionTokenBytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    using var sessionHmac = new HMACSHA256(Encoding.UTF8.GetBytes(otpPepper));
    var sessionTokenHash = Convert.ToHexString(
        sessionHmac.ComputeHash(Encoding.UTF8.GetBytes(sessionToken)));

    var expiresAtUtc = now.AddHours(24);

    db.UserSessions.Add(new UserSession
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenHash = sessionTokenHash,
        CreatedAtUtc = now,
        ExpiresAtUtc = expiresAtUtc,
        RevokedAtUtc = null,
    });

    var executionStrategy = db.Database.CreateExecutionStrategy();
    var consumed = await executionStrategy.ExecuteAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var affectedRows = await db.LoginCodes
            .Where(c =>
                c.Id == loginCode.Id &&
                c.UsedAtUtc == null &&
                c.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.UsedAtUtc, now), cancellationToken);

        if (affectedRows == 0) return false;

        await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.AcceptAllChanges();
        return true;
    });

    if (!consumed)
    {
        return Results.Ok(new VerifyCodeResponse { Success = false });
    }

    return Results.Ok(new VerifyCodeResponse
    {
        Success = true,
        SessionToken = sessionToken,
        ExpiresAtUtc = expiresAtUtc,
    });
})
.WithName("VerifyLoginCode");

app.MapPost("/api/auth/validate-session", async (
    ValidateSessionRequest request,
    DiagLinkSessionService sessionService,
    CancellationToken cancellationToken) =>
{
    var result = await sessionService.ValidateSessionAsync(request.SessionToken, cancellationToken);

    if (result is null)
    {
        return Results.Ok(new ValidateSessionResponse { Valid = false });
    }

    return Results.Ok(new ValidateSessionResponse
    {
        Valid = true,
        UserId = result.UserId,
        ExpiresAtUtc = result.ExpiresAtUtc,
    });
})
.WithName("ValidateSession");

// Returns the caller's own identity — Role/CompanyId/Email come exclusively from claims resolved
// server-side (dbo.Users), never from anything the client supplies. Protected by TechnicianOrAbove:
// any authenticated principal without a resolvable dbo.Users role/company gets 403, not 200 with nulls.
app.MapGet("/api/auth/me", async (
    HttpContext httpContext,
    DiagLinkUserLookupService userLookupService,
    CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirst(DiagLinkClaimTypes.UserId)?.Value;
    var companyId = httpContext.User.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value;
    var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;

    if (string.IsNullOrEmpty(userId) ||
        string.IsNullOrEmpty(companyId) ||
        string.IsNullOrEmpty(role) ||
        !Guid.TryParse(userId, out var parsedUserId))
    {
        return Results.Forbid();
    }

    var user = await userLookupService.FindActiveUserByIdAsync(
        parsedUserId,
        cancellationToken);

    if (user is null)
    {
        return Results.Forbid();
    }

    return Results.Ok(new CurrentUserResponse
    {
        UserId = userId,
        CompanyId = companyId,
        Role = role,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName
    });
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("GetCurrentUser");

// GET /api/companies — every DiagLink client company. Reserved to diaglink_super_admin.
app.MapGet("/api/companies", async (CompanyDirectoryService companyDirectoryService, CancellationToken cancellationToken) =>
{
    var companies = await companyDirectoryService.GetAllCompaniesAsync(cancellationToken);
    return Results.Ok(companies.Select(ToCompanyDto));
})
.RequireAuthorization("SuperAdminOnly")
.WithName("GetCompanies");

// POST /api/companies — creates a company alone (no admin). Reserved to diaglink_super_admin. The
// client only ever supplies Name — Id/Status/timestamps are always generated server-side.
app.MapPost("/api/companies", async (CreateCompanyRequest request, CompanyOnboardingService companyOnboardingService, CancellationToken cancellationToken) =>
{
    var outcome = await companyOnboardingService.CreateCompanyAsync(request.Name, cancellationToken);
    return outcome.Success
        ? Results.Created($"/api/companies/{outcome.Company!.Id}", outcome.Company)
        : MapOnboardingError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("CreateCompany");

// POST /api/companies/{companyId}/admins — creates the first (or an additional) company_admin for an
// existing company. Role/Status/CompanyId are always forced server-side; the client can never send them.
app.MapPost("/api/companies/{companyId:guid}/admins", async (Guid companyId, CreateCompanyAdminRequest request, CompanyOnboardingService companyOnboardingService, CancellationToken cancellationToken) =>
{
    var outcome = await companyOnboardingService.AddCompanyAdminAsync(companyId, request.Email, cancellationToken);
    return outcome.Success
        ? Results.Created($"/api/companies/{companyId}/admins/{outcome.Admin!.Id}", outcome.Admin)
        : MapOnboardingError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("CreateCompanyAdmin");

// POST /api/companies/onboard — transactional: creates the company and its first company_admin together.
// Preferred entry point from the frontend (CompaniesView "Ajouter une entreprise" dialog) since dbo.Companies
// and dbo.Users share the same Azure SQL database, so both inserts can commit or roll back as one unit.
app.MapPost("/api/companies/onboard", async (OnboardCompanyRequest request, CompanyOnboardingService companyOnboardingService, CancellationToken cancellationToken) =>
{
    var outcome = await companyOnboardingService.OnboardCompanyAsync(request.CompanyName, request.AdminEmail, request.FirstName, request.LastName, request.PhoneNumber, cancellationToken);
    return outcome.Success
        ? Results.Created($"/api/companies/{outcome.Result!.Company.Id}", outcome.Result)
        : MapOnboardingError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("OnboardCompany");

// GET /api/company — the caller's own company, resolved exclusively from the company_id claim.
// The client never supplies a CompanyId here.
app.MapGet("/api/company", async (HttpContext httpContext, CompanyDirectoryService companyDirectoryService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var company = await companyDirectoryService.GetCompanyByIdAsync(companyId, cancellationToken);
    return company is null ? Results.NotFound() : Results.Ok(ToCompanyDto(company));
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("GetCurrentCompany");

app.MapGet("/api/company/users", async (HttpContext httpContext, CompanyDirectoryService companyDirectoryService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var users = await companyDirectoryService.GetCompanyUsersAsync(companyId, cancellationToken);
    return Results.Ok(users.Select(ToCompanyUserDto));
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("GetCurrentCompanyUsers");

// POST /api/company/users — company_admin only, creates a technician/company_admin in the caller's
// OWN company (resolved from the company_id claim, never from the request body).
app.MapPost("/api/company/users", async (CreateTechnicianRequest request, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var outcome = await userProvisioningService.CreateTechnicianAsync(companyId, request, cancellationToken);
    return outcome.Success
        ? Results.Created($"/api/company/users/{outcome.User!.Id}", outcome.User)
        : MapUserProvisioningError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("CreateCompanyUser");

app.MapPost("/api/companies/{companyId:guid}/users", async (Guid companyId, CreateTechnicianRequest request, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    var outcome = await userProvisioningService.CreateTechnicianForCompanyAsync(companyId, request, cancellationToken);
    return outcome.Success
        ? Results.Created($"/api/companies/{companyId}/users/{outcome.User!.Id}", outcome.User)
        : MapUserProvisioningError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("CreateCompanyTechnicianForCompany");

// DELETE /api/company/users/{userId} — company_admin only, soft-deletes (Status -> inactive) a user
// in the caller's OWN company. Never reachable for diaglink_super_admin targets or self.
app.MapDelete("/api/company/users/{userId:guid}", async (Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.DeactivateUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("DeleteCompanyUser");

// DELETE /api/companies/{companyId}/users/{userId} — diaglink_super_admin only, soft-deletes
// (Status -> inactive) a user of an explicitly targeted company.
app.MapDelete("/api/companies/{companyId:guid}/users/{userId:guid}", async (Guid companyId, Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.DeactivateUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("DeleteCompanyUserForCompany");

// POST /api/company/users/{userId}/reactivate — company_admin only, reverses the soft-delete above
// (Status -> active) for a user in the caller's OWN company.
app.MapPost("/api/company/users/{userId:guid}/reactivate", async (Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.ReactivateUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("ReactivateCompanyUser");

// POST /api/companies/{companyId}/users/{userId}/reactivate — diaglink_super_admin only.
app.MapPost("/api/companies/{companyId:guid}/users/{userId:guid}/reactivate", async (Guid companyId, Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.ReactivateUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("ReactivateCompanyUserForCompany");

// DELETE /api/company/users/{userId}/permanent — company_admin only. Irreversible: unlike the
// soft-delete above, this removes the dbo.Users row (plus its UserMachines/UserSessions/LoginCodes)
// entirely. Conversations are never touched — see PermanentlyDeleteUserAsync doc comment.
app.MapDelete("/api/company/users/{userId:guid}/permanent", async (Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.PermanentlyDeleteUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("PermanentlyDeleteCompanyUser");

// DELETE /api/companies/{companyId}/users/{userId}/permanent — diaglink_super_admin only.
app.MapDelete("/api/companies/{companyId:guid}/users/{userId:guid}/permanent", async (Guid companyId, Guid userId, HttpContext httpContext, UserProvisioningService userProvisioningService, CancellationToken cancellationToken) =>
{
    TryGetUserIdClaim(httpContext.User, out var callerUserId);
    var outcome = await userProvisioningService.PermanentlyDeleteUserAsync(companyId, userId, callerUserId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapUserDeactivationError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("PermanentlyDeleteCompanyUserForCompany");

// GET /api/company/users/{userId}/machines — every machine of the caller's company, flagged with
// whether userId already has access. userId must belong to the caller's own company_id claim.
app.MapGet("/api/company/users/{userId:guid}/machines", async (Guid userId, HttpContext httpContext, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var outcome = await machineAssignmentService.GetUserMachineAccessAsync(companyId, userId, cancellationToken);
    return outcome.Success ? Results.Ok(outcome.Machines) : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("GetCompanyUserMachines");

// PUT /api/company/users/{userId}/machines/{machineId} — grants access; idempotent (succeeds if already granted).
app.MapPut("/api/company/users/{userId:guid}/machines/{machineId:guid}", async (Guid userId, Guid machineId, HttpContext httpContext, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var outcome = await machineAssignmentService.AssignMachineAsync(companyId, userId, machineId, cancellationToken);
    return outcome.Success ? Results.Ok(new { success = true }) : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("AssignCompanyUserMachine");

// DELETE /api/company/users/{userId}/machines/{machineId} — revokes access; idempotent (succeeds if already absent).
app.MapDelete("/api/company/users/{userId:guid}/machines/{machineId:guid}", async (Guid userId, Guid machineId, HttpContext httpContext, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var outcome = await machineAssignmentService.UnassignMachineAsync(companyId, userId, machineId, cancellationToken);
    return outcome.Success ? Results.NoContent() : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("UnassignCompanyUserMachine");

// PUT /api/company/users/{userId}/machines — replaces the technician's ENTIRE machine access set in one
// transaction. Preferred entry point from the frontend's checkbox UI (see UsersView "Enregistrer les
// accès" button) over per-machine PUT/DELETE calls, since a checkbox grid naturally produces a full
// desired-state list rather than a stream of individual toggle events.
app.MapPut("/api/company/users/{userId:guid}/machines", async (Guid userId, ReplaceUserMachinesRequest request, HttpContext httpContext, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId))
    {
        return Results.Forbid();
    }

    var outcome = await machineAssignmentService.ReplaceUserMachineAccessAsync(companyId, userId, request.MachineIds, cancellationToken);
    return outcome.Success ? Results.Ok(new { success = true }) : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("CompanyAdminOnly")
.WithName("ReplaceCompanyUserMachines");

// GET /api/companies/{companyId}/users — diaglink_super_admin, dbo.Users rows for an explicitly targeted company.
app.MapGet("/api/companies/{companyId:guid}/users", async (Guid companyId, CompanyDirectoryService companyDirectoryService, CancellationToken cancellationToken) =>
{
    var users = await companyDirectoryService.GetCompanyUsersAsync(companyId, cancellationToken);
    return Results.Ok(users.Select(ToCompanyUserDto));
})
.RequireAuthorization("SuperAdminOnly")
.WithName("GetUsersForCompany");

// GET /api/companies/{companyId}/users/{userId}/machines — diaglink_super_admin equivalent of the
// company_admin endpoint above; companyId comes from the route since the super-admin has no tenant claim.
app.MapGet("/api/companies/{companyId:guid}/users/{userId:guid}/machines", async (Guid companyId, Guid userId, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    var outcome = await machineAssignmentService.GetUserMachineAccessAsync(companyId, userId, cancellationToken);
    return outcome.Success ? Results.Ok(outcome.Machines) : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("GetCompanyUserMachinesForCompany");

// PUT /api/companies/{companyId}/users/{userId}/machines — diaglink_super_admin replace-all, same
// transactional semantics as the company_admin version.
app.MapPut("/api/companies/{companyId:guid}/users/{userId:guid}/machines", async (Guid companyId, Guid userId, ReplaceUserMachinesRequest request, MachineAssignmentService machineAssignmentService, CancellationToken cancellationToken) =>
{
    var outcome = await machineAssignmentService.ReplaceUserMachineAccessAsync(companyId, userId, request.MachineIds, cancellationToken);
    return outcome.Success ? Results.Ok(new { success = true }) : MapMachineAssignmentError(outcome.ErrorKind!.Value, outcome.ErrorMessage!);
})
.RequireAuthorization("SuperAdminOnly")
.WithName("ReplaceCompanyUserMachinesForCompany");

// GET /api/machines — role-scoped list (see MachineAccessService); never filtered by anything the client sends.
app.MapGet("/api/machines", async (HttpContext httpContext, MachineAccessService machineAccessService, DiagLinkDbContext db, CancellationToken cancellationToken) =>
{
    var machines = await machineAccessService.GetAccessibleMachinesAsync(httpContext.User, cancellationToken);
    var machineIds = machines.Select(m => m.Machine.Id).ToList();
    // Consider a machine as having an assistant configured if the legacy dbo.Machines row
    // contains a non-empty FoundryAgentId and Status == 'active'. We intentionally no longer
    // depend on chat.MachineAssistantConfigurations for this flag in this migration step.
    var configuredMachineIds = await MachineEntitlements.Eligible(db, DateTime.UtcNow)
        .AsNoTracking()
        .Where(m => machineIds.Contains(m.Id) && !string.IsNullOrEmpty(m.FoundryAgentId) && !string.IsNullOrEmpty(m.ProjectEndpoint))
        .Select(m => m.Id)
        .ToHashSetAsync(cancellationToken);
    return Results.Ok(machines.Select(m => ToMachineDto(m.Machine, configuredMachineIds.Contains(m.Machine.Id), m.IsAccessible)));
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("GetMachines");

// GET /api/machines/{id} — 404 (not 403) when inaccessible, so a caller can't use the status code to
// tell "doesn't exist" apart from "exists in a company/machine they can't see".
app.MapGet("/api/machines/{machineId:guid}/documents", async (Guid machineId, HttpContext httpContext, MachineAccessService machineAccessService, DiagLinkDbContext db, CancellationToken cancellationToken) =>
{
    if (!await machineAccessService.CanAccessMachineAsync(httpContext.User, machineId, cancellationToken))
        return Results.Forbid();
    var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);
    if (machine is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(machine.BlobPrefix))
        return Results.Conflict(new { error = "Machine BlobPrefix is missing." });
    var storage = httpContext.RequestServices.GetService<BlobStorageService>();
    if (storage is null) return Results.StatusCode(503);
    return Results.Ok(await storage.ListMachinePdfDocumentsAsync(machine.BlobPrefix, cancellationToken));
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("GetMachineDocuments");

app.MapGet("/api/machines/{machineId:guid}/documents/{documentId}", async (Guid machineId, string documentId, HttpContext httpContext, MachineAccessService machineAccessService, DiagLinkDbContext db, CancellationToken cancellationToken) =>
{
    if (!await machineAccessService.CanAccessMachineAsync(httpContext.User, machineId, cancellationToken))
        return Results.Forbid();
    var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);
    if (machine is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(machine.BlobPrefix))
        return Results.Conflict(new { error = "Machine BlobPrefix is missing." });
    var storage = httpContext.RequestServices.GetService<BlobStorageService>();
    if (storage is null) return Results.StatusCode(503);
    var stream = await storage.OpenMachineDocumentAsync(machine.BlobPrefix, documentId, cancellationToken);
    if (stream is null) return Results.NotFound();
    httpContext.Response.Headers.CacheControl = "no-store";
    return Results.Stream(stream, "application/pdf");
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("OpenMachineDocument");

app.MapGet("/api/machines/{id:guid}", async (Guid id, HttpContext httpContext, MachineAccessService machineAccessService, DiagLinkDbContext db, CancellationToken cancellationToken) =>
{
    if (!await machineAccessService.CanAccessMachineAsync(httpContext.User, id, cancellationToken))
    {
        return Results.NotFound();
    }

    var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    if (machine is null)
    {
        return Results.NotFound();
    }

    var hasAssistantConfigured = await MachineEntitlements.Eligible(db, DateTime.UtcNow)
        .AsNoTracking()
        .AnyAsync(m => m.Id == id && !string.IsNullOrEmpty(m.FoundryAgentId) && !string.IsNullOrEmpty(m.ProjectEndpoint), cancellationToken);
    return Results.Ok(ToMachineDto(machine, hasAssistantConfigured, true));
})
.RequireAuthorization("TechnicianOrAbove")
.WithName("GetMachineById");

// DEVELOPMENT ONLY: debug endpoint to lookup a Machine by companyName + machineName
if (app.Environment.IsDevelopment())
{
    app.MapGet("/internal/debug/machine", async (string companyName, string machineName, DiagLinkDbContext db, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(machineName))
            return Results.BadRequest(new { error = "companyName and machineName required" });

        var comp = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Name.Trim().ToLower() == companyName.Trim().ToLower(), cancellationToken);
        if (comp is null) return Results.NotFound(new { error = "company not found" });

        var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.CompanyId == comp.Id && m.Name == machineName.Trim(), cancellationToken);
        if (machine is null) return Results.NotFound(new { error = "machine not found" });

        return Results.Ok(machine);
    });

    app.MapPost("/internal/debug/company", async (string companyName, DiagLinkDbContext db, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(companyName)) return Results.BadRequest(new { error = "companyName required" });
        var normalized = companyName.Trim();
        var existing = await db.Companies.FirstOrDefaultAsync(c => c.Name.Trim().ToLower() == normalized.ToLower(), cancellationToken);
        if (existing is not null) return Results.Ok(existing);
        var now = DateTime.UtcNow;
        var company = new WebApp.Api.Models.Entities.Company
        {
            Id = Guid.NewGuid(),
            Name = normalized,
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/internal/debug/company/{company.Id}", company);
    });
}

// Streaming Chat endpoint: Streams agent response via SSE (conversationId → chunks → usage → done)
// Supports MCP tool approval flow with previousResponseId and mcpApproval parameters
app.MapGet("/api/machines/{machineId:guid}/ai-credit", async (Guid machineId, HttpContext context,
    MachineAccessService access, AiCreditAccessService credits, CancellationToken ct) =>
{
    if (!await access.CanAccessMachineAsync(context.User, machineId, ct)) return Results.NotFound();
    return Results.Ok(await credits.CheckAsync(machineId, ct));
}).RequireAuthorization("TechnicianOrAbove");
app.MapPost("/api/chat/stream", async (
    ChatRequest request,
    AiUsagePersistenceBillingService usagePersistenceBilling,
    AiCreditAccessService creditAccess,
    AgentFrameworkService agentService,
    ConversationHistoryRepository historyRepository,
    ConversationSummaryService conversationSummaryService,
    MachineAssistantResolutionService assistantResolutionService,
    UserIdentityService userIdentityService,
    HttpContext httpContext,
    IHostEnvironment environment,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userObjectId = await userIdentityService.GetCanonicalUserObjectIdAsync(httpContext.User, cancellationToken);

    // Resolved strictly server-side from SQL — never trust a client-supplied agent/project. Stays null
    // only for legacy pre-machine-scoping conversations, in which case AgentFrameworkService falls back
    // to the globally-configured (env var) agent.
    ResolvedAssistantConfiguration? resolvedConfig = null;
    Guid? boundMachineId = null;
    var isNewConversation = request.ConversationId is null;
    var measurements = new Dictionary<Guid, AiUsageMeasurement>();

    if (isNewConversation)
    {
        if (request.MachineId is null)
        {
            return Results.BadRequest(new { error = "machine_id_required", message = "machineId is required to start a new conversation." });
        }

        var resolution = await assistantResolutionService.ResolveAsync(httpContext.User, request.MachineId.Value, cancellationToken);
        var earlyExit = MapResolutionFailure(resolution);
        if (earlyExit is not null)
        {
            return earlyExit;
        }

        resolvedConfig = resolution.Configuration;
        boundMachineId = request.MachineId;
    }
    else
    {
        // Ownership can't be verified without a canonical user id — refuse rather than trust the request.
        if (string.IsNullOrEmpty(userObjectId))
        {
            return Results.NotFound();
        }

        var ownership = await historyRepository.GetConversationOwnershipInfoAsync(request.ConversationId!, userObjectId, cancellationToken);
        if (ownership is null)
        {
            return Results.NotFound();
        }

        if (ownership.MachineId.HasValue)
        {
            if (request.MachineId.HasValue && request.MachineId.Value != ownership.MachineId.Value)
            {
                return Results.BadRequest(new { error = "machine_id_mismatch", message = "machineId does not match this conversation's bound machine." });
            }

            // Re-verified on every message, not just at creation — access revoked mid-conversation must
            // refuse immediately rather than keep streaming against a machine the user can no longer see.
            var resolution = await assistantResolutionService.ResolveAsync(httpContext.User, ownership.MachineId.Value, cancellationToken);
            var earlyExit = MapResolutionFailure(resolution);
            if (earlyExit is not null)
            {
                return earlyExit;
            }

            resolvedConfig = resolution.Configuration;
            boundMachineId = ownership.MachineId;
        }
    }

    // Admission is checked after ownership resolution, before any provider call or message write.
    var admission = await creditAccess.CheckAsync(boundMachineId, cancellationToken);
    if (!admission.Allowed)
        return Results.Json(new { status = admission.Status, code = admission.Status, message = admission.Message }, statusCode: 402);

    try
    {
        httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("Connection", "keep-alive");

        var conversationId = request.ConversationId
            ?? await agentService.CreateConversationAsync(request.Message, resolvedConfig?.ProjectEndpoint, cancellationToken);

        await WriteConversationIdEvent(httpContext.Response, conversationId, cancellationToken);

        if (isNewConversation)
        {
            try
            {
                if (!string.IsNullOrEmpty(userObjectId))
                {
                    var agentName = (await agentService.GetAgentMetadataAsync(resolvedConfig, cancellationToken)).Name;
                    await historyRepository.CreateConversationAsync(conversationId, userObjectId, agentName, boundMachineId, cancellationToken);
                }
                else
                {
                    logger.LogWarning("No 'oid' claim on ClaimsPrincipal; conversation {ConversationId} not persisted.", conversationId);
                }
            }
            catch (Exception ex)
            {
                // Best-effort: SQL persistence failures must never break the Foundry chat stream.
                logger.LogError(ex, "Failed to persist new conversation {ConversationId}", conversationId);
            }
        }

        // Best-effort: build the model-bound message from SQL memory (TechnicalSummary + recent
        // non-summarized messages) BEFORE saving request.Message below — this guarantees the current
        // question is never present twice (once live, once re-read back from its own just-saved row).
        var messageForModel = request.Message;
        if (request.McpApproval is null && !string.IsNullOrEmpty(userObjectId))
        {
            try
            {
                var technicalSummary = await historyRepository.GetTechnicalSummaryForUserAsync(conversationId, userObjectId, cancellationToken);
                var recentMessages = await historyRepository.GetRecentUnsummarizedMessagesForUserAsync(
                    conversationId, userObjectId, ConversationSummaryService.KeepRawMessageCount, cancellationToken);
                messageForModel = ConversationContextBuilder.BuildMessage(technicalSummary, recentMessages, request.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to build SQL memory context for conversation {ConversationId}; continuing without it.", conversationId);
            }
        }

        // Skip MCP-approval resumes ("Approved"/"Rejected") — not a real user message.
        if (request.McpApproval is null && !string.IsNullOrEmpty(userObjectId))
        {
            try
            {
                await historyRepository.AddMessageAsync(conversationId, userObjectId, "user", request.Message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist user message for conversation {ConversationId}", conversationId);
            }
        }

        var startTime = DateTime.UtcNow;
        var assistantText = new StringBuilder();
        Guid? sqlConversationId = null;
        // Captured from the authorized machine, never the caller's company claim.
        var machineCompanyId = resolvedConfig?.CompanyId;
        var sqlUserId = Guid.TryParse(httpContext.User.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var parsedUserId)
            ? (Guid?)parsedUserId : null;
        try
        {
            sqlConversationId = await historyRepository.GetConversationIdByFoundryIdAsync(conversationId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL usage context unavailable for conversation {ConversationId}", conversationId);
        }
        // Request-local final state, persisted once per event in finally, including interrupted responses with an ID.
        var measurement = new AiUsageMeasurement(sqlUserId, machineCompanyId, boundMachineId,
            sqlConversationId, conversationId, null,
            new AiResponseUsage(AiUsageType.ChatResponse, null, false, null, null, null,
                null, null, null, DateTimeOffset.UtcNow));
        measurements[measurement.EventId] = measurement;
        httpContext.Items[typeof(AiUsageMeasurement)] = measurements;

        await foreach (var chunk in agentService.StreamMessageAsync(
            conversationId,
            messageForModel,
            request.ImageDataUris,
            request.FileDataUris,
            request.PreviousResponseId,
            request.McpApproval,
            resolvedConfig,
            cancellationToken))
        {
            if (chunk.VisionUsage is { } vision)
            {
                measurements[vision.EventId] = vision.WithContext(measurement);
            }
            else if (chunk.Usage is not null)
            {
                measurement = measurement with { Response = chunk.Usage };
                measurements[measurement.EventId] = measurement;
            }
            else if (chunk.IsText && chunk.TextDelta != null)
            {
                assistantText.Append(chunk.TextDelta);
                await WriteChunkEvent(httpContext.Response, chunk.TextDelta, cancellationToken);
            }
            else if (chunk.HasAnnotations && chunk.Annotations != null)
            {
                await WriteAnnotationsEvent(httpContext.Response, chunk.Annotations, cancellationToken);
            }
            else if (chunk.IsMcpApprovalRequest && chunk.McpApprovalRequest != null)
            {
                await WriteMcpApprovalRequestEvent(httpContext.Response, chunk.McpApprovalRequest, cancellationToken);
            }
            else if (chunk.IsToolUse && chunk.ToolName != null)
            {
                await WriteToolUseEvent(httpContext.Response, chunk.ToolName, cancellationToken);
            }
        }

        if (!string.IsNullOrEmpty(userObjectId))
        {
            try
            {
                var assistantMessageId = await historyRepository.AddMessageAsync(conversationId, userObjectId, "assistant", assistantText.ToString(), cancellationToken);
                measurement = measurement with { AssistantMessageId = assistantMessageId };
                measurements[measurement.EventId] = measurement;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist assistant message for conversation {ConversationId}", conversationId);
            }
        }

        try
        {
            if (sqlConversationId.HasValue)
            {
                var summaryEventId = Guid.NewGuid();
                await conversationSummaryService.MaybeSummarizeAsync(sqlConversationId.Value, cancellationToken,
                    summaryUsage =>
                    {
                        var summaryMeasurement = measurement with { EventId = summaryEventId, AssistantMessageId = null, Response = summaryUsage };
                        measurements[summaryEventId] = summaryMeasurement;
                    });
            }
            else
            {
                logger.LogWarning("No SQL row found for FoundryConversationId {ConversationId}; skipping summarization.", conversationId);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: summarization failures must never break the Foundry chat stream.
            logger.LogError(ex, "Conversation summarization failed for conversation {ConversationId}", conversationId);
        }

        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        await WriteUsageEvent(
            httpContext.Response,
            duration,
            measurement.Response,
            cancellationToken);

        await WriteDoneEvent(httpContext.Response, cancellationToken);
    }
    catch (ArgumentException ex) when (ex.Message.Contains("Invalid") && (ex.Message.Contains("attachments") || ex.Message.Contains("image") || ex.Message.Contains("file")))
    {
        // Validation errors from image/file processing - return 400 Bad Request
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex, 
            400, 
            environment.IsDevelopment());
        
        await WriteErrorEvent(
            httpContext.Response, 
            errorResponse.Detail ?? errorResponse.Title, 
            cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Chat stream error: {Message}", ex.Message);
        
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex, 
            500, 
            environment.IsDevelopment());
        
        await WriteErrorEvent(
            httpContext.Response, 
            errorResponse.Detail ?? errorResponse.Title, 
            cancellationToken);
    }

    finally
    {
        // RequestAborted must not discard a completed/identified response after an SSE disconnect.
        await usagePersistenceBilling.ProcessAsync(measurements.Values);
    }

    return Results.Empty;

    // Maps a non-Configured resolution outcome to its HTTP response; returns null when Configured
    // (meaning the caller should proceed). 404 for inaccessible (anti-enumeration, matches
    // /api/machines/{id}); 409 for a real-but-unusable machine, never leaking AgentId/ProjectEndpoint.
    static IResult? MapResolutionFailure(MachineAssistantResolution resolution) => resolution.Kind switch
    {
        MachineAssistantResolutionKind.MachineNotAccessible => Results.NotFound(),
        MachineAssistantResolutionKind.AssistantNotConfigured or MachineAssistantResolutionKind.AssistantDisabled =>
            Results.Json(new { error = "assistant_not_configured", message = "No assistant is configured for this machine yet." }, statusCode: 409),
        _ => null,
    };
})
.RequireAuthorization(ScopePolicyName)
.WithName("StreamChatMessage");

static async Task WriteConversationIdEvent(HttpResponse response, string conversationId, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "conversationId", conversationId });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteChunkEvent(HttpResponse response, string content, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "chunk", content });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteToolUseEvent(HttpResponse response, string toolName, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "toolUse", toolName });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteAnnotationsEvent(HttpResponse response, List<WebApp.Api.Models.AnnotationInfo> annotations, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "annotations",
        annotations = annotations.Select(a => new
        {
            type = a.Type,
            label = a.Label,
            url = a.Url,
            fileId = a.FileId,
            containerId = a.ContainerId,
            textToReplace = a.TextToReplace,
            startIndex = a.StartIndex,
            endIndex = a.EndIndex,
            quote = a.Quote
        })
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteMcpApprovalRequestEvent(HttpResponse response, WebApp.Api.Models.McpApprovalRequest approval, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "mcpApprovalRequest",
        approvalRequest = new
        {
            id = approval.Id,
            toolName = approval.ToolName,
            serverLabel = approval.ServerLabel,
            arguments = approval.Arguments,
            previousResponseId = approval.PreviousResponseId
        }
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteUsageEvent(HttpResponse response, double duration, AiResponseUsage usage, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "usage",
        duration,
        available = usage.Available,
        completed = usage.Completed,
        promptTokens = usage.InputTokens,
        completionTokens = usage.OutputTokens,
        totalTokens = usage.TotalTokens,
        model = usage.Model,
        modelSource = usage.ModelSource,
        agentVersion = usage.AgentVersion
    });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteDoneEvent(HttpResponse response, CancellationToken ct)
{
    await response.WriteAsync("data: {\"type\":\"done\"}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

static async Task WriteErrorEvent(HttpResponse response, string message, CancellationToken ct)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new { type = "error", message });
    await response.WriteAsync($"data: {json}\n\n", ct);
    await response.Body.FlushAsync(ct);
}

// Get agent metadata (name, description, model, metadata)
// Used by frontend to display agent information in the UI
app.MapGet("/api/agent", async (
    HttpContext httpContext,
    Guid machineId,
    MachineAssistantResolutionService assistantResolutionService,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var resolution = await assistantResolutionService.ResolveAsync(httpContext.User, machineId, cancellationToken);

        // Same mapping as /api/chat/stream: 404 anti-enumeration, 409 for a real-but-unusable machine.
        var earlyExit = resolution.Kind switch
        {
            MachineAssistantResolutionKind.MachineNotAccessible => Results.NotFound(),
            MachineAssistantResolutionKind.AssistantNotConfigured or MachineAssistantResolutionKind.AssistantDisabled =>
                Results.Json(new { error = "assistant_not_configured", message = "No assistant is configured for this machine yet." }, statusCode: 409),
            _ => (IResult?)null,
        };
        if (earlyExit is not null)
        {
            return earlyExit;
        }

        var metadata = await agentService.GetAgentMetadataAsync(
            resolution.Configuration,
            cancellationToken);

        return Results.Ok(metadata);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex,
            500,
            environment.IsDevelopment());

        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAgentMetadata");

// Get agent info (for debugging)
app.MapGet("/api/agent/info", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var agentInfo = await agentService.GetAgentInfoAsync(cancellationToken);
        return Results.Ok(new
        {
            info = agentInfo,
            status = "ready"
        });
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(
            ex, 
            500, 
            environment.IsDevelopment());
        
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetAgentInfo");

// List conversations — reads the SQL history (source of truth for the UI), strictly scoped to the
// authenticated user's oid. Foundry itself remains the source of truth for the live chat conversation.
app.MapGet("/api/conversations", async (
    ConversationHistoryRepository historyRepository,
    HttpContext httpContext,
    IHostEnvironment environment,
    int? limit,
    CancellationToken cancellationToken) =>
{
    try
    {
        var userIdentityService = httpContext.RequestServices.GetRequiredService<UserIdentityService>();
        var userObjectId = await userIdentityService.GetCanonicalUserObjectIdAsync(httpContext.User, cancellationToken);
        if (string.IsNullOrEmpty(userObjectId))
        {
            return Results.Ok(new { conversations = Array.Empty<WebApp.Api.Models.ConversationSummary>(), hasMore = false });
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var allConversations = await historyRepository.ListConversationsForUserAsync(userObjectId, cancellationToken);
        var hasMore = allConversations.Count > pageSize;
        var conversations = hasMore ? allConversations.Take(pageSize).ToList() : allConversations;
        return Results.Ok(new { conversations, hasMore });
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("ListConversations");

// Get conversation messages — reads the SQL history. {conversationId} is the FoundryConversationId.
// Ownership (UserObjectId) is checked in the same query as the lookup; a missing or
// not-owned conversation both yield 404, so existence of another user's conversation is never revealed.
app.MapGet("/api/conversations/{conversationId}/messages", async (
    string conversationId,
    ConversationHistoryRepository historyRepository,
    HttpContext httpContext,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var userIdentityService = httpContext.RequestServices.GetRequiredService<UserIdentityService>();
        var userObjectId = await userIdentityService.GetCanonicalUserObjectIdAsync(httpContext.User, cancellationToken);
        if (string.IsNullOrEmpty(userObjectId))
        {
            return Results.NotFound();
        }

        var messages = await historyRepository.GetConversationMessagesAsync(conversationId, userObjectId, cancellationToken);
        if (messages is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(messages);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("GetConversationMessages");

// Delete conversation
app.MapDelete("/api/conversations/{conversationId}", async (
    string conversationId,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        await agentService.DeleteConversationAsync(conversationId, cancellationToken);
        return Results.NoContent();
    }
    catch (NotSupportedException)
    {
        return Results.Problem(
            title: "Not Implemented",
            detail: "Conversation deletion is not yet supported by the Azure.AI.Projects SDK.",
            statusCode: 501
        );
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("DeleteConversation");

// File download endpoint for code interpreter outputs
app.MapGet("/api/files/{fileId}", async (
    string fileId,
    string? containerId,
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var (content, fileName) = await agentService.DownloadFileAsync(fileId, containerId, cancellationToken);
        var contentType = GetMimeType(fileName);
        return Results.File(content.ToArray(), contentType, fileName);
    }
    catch (HttpRequestException httpEx)
    {
        var statusCode = (int?)httpEx.StatusCode ?? 502;
        var errorResponse = ErrorResponseFactory.CreateFromException(httpEx, statusCode, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("DownloadFile");

// Uploaded-files cleanup endpoints — inspect & delete image files previously uploaded by
// this web app. Uses the WebAppUploadFilenamePrefix tag applied on upload to scope the
// operation to our own files, because the Foundry Files API does not expose a typed
// expires_after parameter in the GA SDK (see README "Known limitations").
app.MapGet("/api/files/uploaded", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var info = await agentService.ListUploadedFilesAsync(cancellationToken);
        return Results.Ok(info);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("ListUploadedFiles");

app.MapPost("/api/files/cleanup", async (
    AgentFrameworkService agentService,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await agentService.CleanupUploadedFilesAsync(cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        var errorResponse = ErrorResponseFactory.CreateFromException(ex, 500, environment.IsDevelopment());
        return Results.Problem(
            title: errorResponse.Title,
            detail: errorResponse.Detail,
            statusCode: errorResponse.Status,
            extensions: errorResponse.Extensions
        );
    }
})
.RequireAuthorization(ScopePolicyName)
.WithName("CleanupUploadedFiles");

// Fallback route for SPA - serve index.html for any non-API routes
app.MapFallbackToFile("index.html");

app.MapGet("/api/admin/usage/summary", async (string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter))
        return Results.BadRequest(new { error = "Période, type ou identifiant invalide. Utiliser des dates ISO 8601 avec fuseau et des GUID ou unassigned." });
    return Results.Ok(await service.SummaryAsync(filter, ct));
}).RequireAuthorization("SuperAdminOnly");

app.MapGet("/api/admin/usage/companies", async (string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter))
        return Results.BadRequest(new { error = "Période, type ou identifiant invalide. Utiliser des dates ISO 8601 avec fuseau et des GUID ou unassigned." });
    return Results.Ok(await service.CompaniesAsync(filter, ct));
}).RequireAuthorization("SuperAdminOnly");

app.MapGet("/api/admin/usage/companies/{companyId}/machines", async (string companyId, string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter) || !AiUsageFilter.TryScope(companyId, out var company))
        return Results.BadRequest(new { error = "Période, type ou identifiant invalide. Utiliser des dates ISO 8601 avec fuseau et des GUID ou unassigned." });
    return Results.Ok(await service.MachinesAsync(filter, company, ct));
}).RequireAuthorization("SuperAdminOnly");

app.MapGet("/api/admin/usage/machines/{machineId}/users", async (string machineId, string? companyId, string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter) || !AiUsageFilter.TryScope(companyId, out var company) || !AiUsageFilter.TryScope(machineId, out var machine))
        return Results.BadRequest(new { error = "Période, type ou identifiant invalide. Utiliser des dates ISO 8601 avec fuseau et des GUID ou unassigned." });
    return Results.Ok(await service.UsersAsync(filter, company, machine, ct));
}).RequireAuthorization("SuperAdminOnly");


app.MapGet("/api/company/usage/summary", async (HttpContext httpContext, string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId)) return Results.Forbid();
    if (httpContext.Request.Query.ContainsKey("companyId"))
        return Results.BadRequest(new { error = "Le périmètre entreprise est imposé par l'identité serveur." });
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter)) return Results.BadRequest();

    return Results.Ok(await service.CompanySummaryAsync(filter, companyId, ct));
}).RequireAuthorization("CompanyAdminOnly");

app.MapGet("/api/company/usage/machines", async (HttpContext httpContext, string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId)) return Results.Forbid();
    if (httpContext.Request.Query.ContainsKey("companyId"))
        return Results.BadRequest(new { error = "Le périmètre entreprise est imposé par l'identité serveur." });
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter)) return Results.BadRequest();

    return Results.Ok(await service.MachinesAsync(filter, companyId, ct));
}).RequireAuthorization("CompanyAdminOnly");

app.MapGet("/api/company/usage/machines/{machineId}/users", async (string machineId, MachineAccessService access, HttpContext httpContext, string? from, string? to, string? usageType, AiUsageQueryService service, CancellationToken ct) =>
{
    if (!TryGetCompanyIdClaim(httpContext.User, out var companyId)) return Results.Forbid();
    if (httpContext.Request.Query.ContainsKey("companyId"))
        return Results.BadRequest(new { error = "Le périmètre entreprise est imposé par l'identité serveur." });
    if (!AiUsageFilter.TryParse(from, to, usageType, out var filter)) return Results.BadRequest();

    if (!AiUsageFilter.TryScope(machineId, out var machine)) return Results.BadRequest();
    if (machine.HasValue && !await access.CanAccessMachineAsync(httpContext.User, machine.Value, ct))
        return Results.Forbid();

    return Results.Ok(await service.UsersAsync(filter, companyId, machine, ct));
}).RequireAuthorization("CompanyAdminOnly");


app.Run();

static IResult OtpRateLimitExceeded(HttpContext httpContext, OtpRateLimitDecision decision)
{
    var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((decision.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds));
    httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return Results.Json(new { error = "Trop de tentatives. Veuillez réessayer plus tard." },
        statusCode: StatusCodes.Status429TooManyRequests);
}

// Maps a Company entity to its API contract — never expose EF entities directly.
static CompanyDto ToCompanyDto(Company company) => new()
{
    Id = company.Id.ToString(),
    Name = company.Name,
    Status = company.Status,
};

// Minimal dbo.Users projection — deliberately excludes EntraObjectId.
static CompanyUserDto ToCompanyUserDto(User user) => new()
{
    Id = user.Id.ToString(),
    Email = user.Email,
    Role = user.Role,
    Status = user.Status,
    FirstName = user.FirstName,
    LastName = user.LastName,
    PhoneNumber = user.PhoneNumber,
};

static MachineDto ToMachineDto(Machine machine, bool hasAssistantConfigured, bool isAccessible) => new()
{
    Id = machine.Id.ToString(),
    CompanyId = machine.CompanyId.ToString(),
    Name = machine.Name,
    Reference = machine.Reference,
    Status = machine.Status,
    HasAssistantConfigured = hasAssistantConfigured,
    IsAccessible = isAccessible,
};

static bool TryGetCompanyIdClaim(ClaimsPrincipal user, out Guid companyId)
    => Guid.TryParse(user.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value, out companyId);

static bool TryGetUserIdClaim(ClaimsPrincipal user, out Guid? userId)
{
    if (Guid.TryParse(user.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var parsed))
    {
        userId = parsed;
        return true;
    }

    userId = null;
    return false;
}

// Maps a CompanyOnboardingService validation/conflict outcome to its HTTP status — never leaks SQL details.
static IResult MapOnboardingError(CompanyOnboardingErrorKind kind, string message) => kind switch
{
    CompanyOnboardingErrorKind.CompanyNotFound => Results.NotFound(new { error = message }),
    CompanyOnboardingErrorKind.DuplicateCompanyName or CompanyOnboardingErrorKind.DuplicateEmail => Results.Conflict(new { error = message }),
    CompanyOnboardingErrorKind.TechnicalError => Results.Problem(title: message, statusCode: StatusCodes.Status500InternalServerError),
    _ => Results.BadRequest(new { error = message }),
};

static IResult MapUserProvisioningError(UserProvisioningErrorKind kind, string message) => kind switch
{
    UserProvisioningErrorKind.CompanyNotFound => Results.NotFound(new { error = message }),
    UserProvisioningErrorKind.DuplicateEmail => Results.Conflict(new { error = message }),
    _ => Results.BadRequest(new { error = message }),
};

// UserNotFound uses 404 (anti-enumeration, matches /api/companies/{id}); the two "can't delete this
// target" cases use 409 — the resource exists but the operation is refused, never a 403 that would
// hint whether the target exists to a caller who otherwise wouldn't be able to tell.
static IResult MapUserDeactivationError(UserDeactivationErrorKind kind, string message) => kind switch
{
    UserDeactivationErrorKind.UserNotFound => Results.NotFound(new { error = message }),
    UserDeactivationErrorKind.CannotDeleteSuperAdmin or UserDeactivationErrorKind.CannotDeleteSelf =>
        Results.Conflict(new { error = message }),
    _ => Results.BadRequest(new { error = message }),
};

static IResult MapMachineAssignmentError(MachineAssignmentErrorKind kind, string message) => kind switch
{
    MachineAssignmentErrorKind.UserNotFound or MachineAssignmentErrorKind.MachineNotFound => Results.NotFound(new { error = message }),
    _ => Results.BadRequest(new { error = message }),
};

// Helper to determine MIME type from file extension
static string GetMimeType(string fileName)
{
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    return ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".html" => "text/html",
        ".py" => "text/x-python",
        ".js" => "text/javascript",
        _ => "application/octet-stream",
    };
}

