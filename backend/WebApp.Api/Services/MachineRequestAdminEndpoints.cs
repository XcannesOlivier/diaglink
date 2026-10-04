using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static class MachineRequestAdminEndpoints
{
    public const string BaseRoute = "/api/admin/machine-requests";

    public static void MapAdminMachineRequests(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BaseRoute).RequireAuthorization("SuperAdminOnly");
        group.MapGet("", ListAsync);
        group.MapGet("/history", ListArchivedAsync);
        group.MapGet("/{requestId}", GetAsync);
        group.MapGet("/{requestId}/documents/{documentId}", DownloadDocumentAsync);
        group.MapPatch("/{requestId}/status", UpdateStatusAsync);
        group.MapPatch("/{requestId}/archive", UpdateArchiveAsync);
        group.MapPatch("/{requestId}/provisioning/business-entities", AttachBusinessEntitiesAsync);
        group.MapPost("/{requestId}/provisioning/customer", LinkCustomerAsync);
        group.MapPost("/{requestId}/provisioning/subscription", ConfigureSubscriptionAsync);
        group.MapPost("/{requestId}/provisioning/activate", ActivateAsync);
        group.MapPost("/{requestId}/additional-machine/accept", AcceptAdditionalAsync);
        group.MapPost("/{requestId}/additional-machine/reject", RejectAdditionalAsync);
        group.MapPost("/{requestId}/additional-documents/accept", AcceptAdditionalDocumentsAsync);
        group.MapPost("/{requestId}/additional-documents/reject", RejectAdditionalDocumentsAsync);
        group.MapPost("/{requestId}/ready", MarkReadyAsync);
    }

    public static async Task<IResult> ListAsync(
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var requests = (await storageService.ListAsync(cancellationToken)).Where(request => !request.IsArchived).ToList();
        return Results.Ok((await VisibleAsync(requests, paymentService, cancellationToken)).Select(ToListItem));
    }

    public static async Task<IResult> ListArchivedAsync(
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var requests = (await storageService.ListAsync(cancellationToken))
            .Where(request => request.IsArchived)
            .OrderByDescending(request => request.ArchivedAtUtc ?? request.CreatedAt)
            .ToList();
        return Results.Ok((await VisibleAsync(requests, paymentService, cancellationToken)).Select(ToListItem));
    }

    private static async Task<IReadOnlyList<MachineRequestRecord>> VisibleAsync(
        IReadOnlyList<MachineRequestRecord> requests,
        MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var visible = new List<MachineRequestRecord>(requests.Count);
        foreach (var request in requests)
        {
            if (request.RequestKind != MachineRequestKind.AdditionalDocuments)
            {
                visible.Add(request);
                continue;
            }
            if (request.Payment is null) continue;
            var payment = await paymentService.ReadAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (payment is not null && payment.Status != "pending") visible.Add(request);
        }
        return visible;
    }

    public static async Task<IResult> GetAsync(
        string requestId,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();
            var payment = request.Payment is null
                ? null
                : await paymentService.ReadAsync(request.Payment.PaymentRequestId, cancellationToken);
            return Results.Ok(ToDetail(request, payment));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." });
        }
    }

    public static async Task<IResult> DownloadDocumentAsync(
        string requestId,
        string documentId,
        [FromServices] MachineRequestStorageService storageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();

            var document = request.Documents.SingleOrDefault(item =>
                string.Equals(CreateDocumentId(item.BlobName), documentId, StringComparison.Ordinal));
            if (document is null) return Results.NotFound();

            var download = await storageService.OpenDocumentAsync(requestId, document.BlobName, cancellationToken);
            return download is null
                ? Results.NotFound()
                : Results.File(download.Content, download.ContentType, download.OriginalName, enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." });
        }
    }

    public static async Task<IResult> UpdateStatusAsync(
        string requestId,
        [FromBody] MachineRequestStatusUpdate update,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (update is null || !MachineRequestStatuses.IsValid(update.Status))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Le statut doit être pending, treated ou rejected."] });

        try
        {
            var existing = await storageService.GetAsync(requestId, cancellationToken);
            if (existing is null) return Results.NotFound();
            if (existing.RequestKind == MachineRequestKind.AdditionalDocuments)
                return Results.Conflict(new
                {
                    error = "Les demandes AdditionalDocuments doivent utiliser les actions dédiées d'acceptation ou de refus."
                });
            MachineRequestPaymentResult? payment = null;
            if (existing.Payment is not null)
            {
                payment = await paymentService.ReadAsync(existing.Payment.PaymentRequestId, cancellationToken);
                if (update.Status is MachineRequestStatuses.Treated or MachineRequestStatuses.Rejected)
                {
                    var requiredPaymentStatus = update.Status == MachineRequestStatuses.Treated ? "captured" : "cancelled";
                    if (payment?.Status != requiredPaymentStatus)
                        return Results.Conflict(new { error = $"Le paiement doit être {requiredPaymentStatus} avant ce changement de statut." });
                }
            }
            var request = await storageService.UpdateStatusAsync(requestId, update.Status, cancellationToken);
            return request is null ? Results.NotFound() : Results.Ok(ToDetail(request, payment));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." });
        }
    }

    public static async Task<IResult> UpdateArchiveAsync(
        string requestId,
        [FromBody] MachineRequestArchiveUpdate update,
        HttpContext httpContext,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var userId))
            return Results.Forbid();
        try
        {
            var existing = await storageService.GetAsync(requestId, cancellationToken);
            if (existing is null) return Results.NotFound();
            if (update.IsArchived && existing.Status is not (MachineRequestStatuses.Treated or MachineRequestStatuses.Rejected))
                return Results.Conflict(new { error = "Seules les demandes traitées ou refusées peuvent être archivées." });
            var request = await storageService.UpdateArchiveAsync(requestId, update.IsArchived, userId, cancellationToken);
            if (request is null) return Results.NotFound();
            var payment = request.Payment is null
                ? null
                : await paymentService.ReadAsync(request.Payment.PaymentRequestId, cancellationToken);
            return Results.Ok(ToDetail(request, payment));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." });
        }
    }

    public static async Task<IResult> AttachBusinessEntitiesAsync(
        string requestId,
        [FromBody] MachineRequestBusinessEntitiesUpdate update,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (update is null || update.CompanyId == Guid.Empty || update.MachineId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["businessEntities"] = ["Une entreprise et une machine valides sont requises."] });
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Payment is null)
                return Results.Conflict(new { error = "Cette demande historique ne possède pas de paiement à provisionner." });
            var linkedPayment = await paymentService.ReadPaymentAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (linkedPayment is null) return Results.NotFound();
            if (!string.Equals(linkedPayment.MachineRequestId, requestId, StringComparison.Ordinal))
                return Results.Conflict(new { error = "Le paiement n'est pas lié à cette demande machine." });
            var result = await paymentService.AttachBusinessEntitiesAsync(request.Payment.PaymentRequestId,
                update.CompanyId, update.MachineId, cancellationToken);
            if (result is null) return Results.NotFound();
            var refreshed = await storageService.GetAsync(requestId, cancellationToken);
            return refreshed is null ? Results.NotFound() : Results.Ok(ToDetail(refreshed, result));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    public static async Task<IResult> LinkCustomerAsync(
        string requestId,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestCustomerLinkService customerLinkService,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Payment is null)
                return Results.Conflict(new { error = "Cette demande historique ne possède pas de paiement à provisionner." });
            var linkedPayment = await paymentService.ReadPaymentAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (linkedPayment is null) return Results.NotFound();
            if (!string.Equals(linkedPayment.MachineRequestId, requestId, StringComparison.Ordinal))
                return Results.Conflict(new { error = "Le paiement n'est pas lié à cette demande machine." });
            var result = await customerLinkService.LinkAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (result is null) return Results.NotFound();
            var refreshed = await storageService.GetAsync(requestId, cancellationToken);
            var displayed = await paymentService.ReadAsync(result.PaymentRequestId, cancellationToken);
            return refreshed is null ? Results.NotFound() : Results.Ok(ToDetail(refreshed, displayed));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    public static async Task<IResult> ConfigureSubscriptionAsync(
        string requestId,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Payment is null) return Results.Conflict(new { error = "Cette demande historique ne possède pas de paiement à provisionner." });
            var payment = await paymentService.ReadPaymentAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (payment is null) return Results.NotFound();
            if (!string.Equals(payment.MachineRequestId, requestId, StringComparison.Ordinal))
                return Results.Conflict(new { error = "Le paiement n'est pas lié à cette demande machine." });
            var result = await subscriptionService.ConfigureAsync(payment.PaymentRequestId, cancellationToken);
            if (result is null) return Results.NotFound();
            var refreshed = await storageService.GetAsync(requestId, cancellationToken);
            var displayed = await paymentService.ReadAsync(result.PaymentRequestId, cancellationToken);
            return refreshed is null ? Results.NotFound() : Results.Ok(ToDetail(refreshed, displayed));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    public static async Task<IResult> ActivateAsync(string requestId,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestActivationService activationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await storageService.GetAsync(requestId, cancellationToken);
            if (request is null) return Results.NotFound();
            if (request.Payment is null) return Results.Conflict(new { error = "Cette demande historique ne possède pas de paiement à provisionner." });
            var payment = await paymentService.ReadPaymentAsync(request.Payment.PaymentRequestId, cancellationToken);
            if (payment is null) return Results.NotFound();
            if (!string.Equals(payment.MachineRequestId, requestId, StringComparison.Ordinal))
                return Results.Conflict(new { error = "Le paiement n'est pas lié à cette demande machine." });
            var result = await activationService.ActivateAsync(payment.PaymentRequestId, cancellationToken);
            if (result is null) return Results.NotFound();
            var refreshed = await storageService.GetAsync(requestId, cancellationToken);
            var displayed = await paymentService.ReadAsync(result.PaymentRequestId, cancellationToken);
            return refreshed is null ? Results.NotFound() : Results.Ok(ToDetail(refreshed, displayed));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    public static Task<IResult> AcceptAdditionalAsync(string requestId,
        [FromServices] AdditionalMachineRequestDecisionService decisions, CancellationToken ct) =>
        DecideAdditionalAsync(requestId, decisions.AcceptAsync, ct);

    public static Task<IResult> RejectAdditionalAsync(string requestId,
        [FromServices] AdditionalMachineRequestDecisionService decisions, CancellationToken ct) =>
        DecideAdditionalAsync(requestId, decisions.RejectAsync, ct);

    private static async Task<IResult> DecideAdditionalAsync(string requestId,
        Func<string, CancellationToken, Task<AdditionalMachineRequestDecision?>> action, CancellationToken ct)
    {
        try
        {
            var result = await action(requestId, ct);
            return result is null ? Results.NotFound() : Results.Ok(ToDetail(result.Request, result.Payment));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    public static Task<IResult> AcceptAdditionalDocumentsAsync(string requestId,
        [FromServices] AdditionalDocumentsRequestDecisionService decisions, CancellationToken ct) =>
        DecideAdditionalDocumentsAsync(requestId, decisions.AcceptAsync, ct);

    public static Task<IResult> RejectAdditionalDocumentsAsync(string requestId,
        [FromServices] AdditionalDocumentsRequestDecisionService decisions, CancellationToken ct) =>
        DecideAdditionalDocumentsAsync(requestId, decisions.RejectAsync, ct);

    public static async Task<IResult> MarkReadyAsync(string requestId, HttpContext httpContext,
        [FromServices] MachineRequestStorageService storage,
        [FromServices] MachineRequestPaymentService payments,
        [FromServices] MachineRequestPaymentStore store,
        CancellationToken ct)
    {
        if (!Guid.TryParse(httpContext.User.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out var userId))
            return Results.Forbid();
        try
        {
            var request = await storage.GetAsync(requestId, ct);
            if (request is null) return Results.NotFound();
            if (request.Payment is null || request.Status == MachineRequestStatuses.Rejected)
                return Results.Conflict(new { error = "La demande acceptée est requise." });
            var payment = await payments.ReadPaymentAsync(request.Payment.PaymentRequestId, ct);
            if (payment is null) return Results.NotFound();
            var ready = await store.MarkReadyAsync(payment, request, userId, ct);
            var displayed = await payments.ReadAsync(ready.PaymentRequestId, ct);
            return Results.Ok(ToDetail(request, displayed));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L’identifiant de la demande est invalide." }); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }

    private static async Task<IResult> DecideAdditionalDocumentsAsync(string requestId,
        Func<string, CancellationToken, Task<AdditionalDocumentsRequestDecision?>> action, CancellationToken ct)
    {
        try
        {
            var result = await action(requestId, ct);
            return result is null ? Results.NotFound() : Results.Ok(ToDetail(result.Request, result.Payment));
        }
        catch (ArgumentException) { return Results.BadRequest(new { error = "L'identifiant de la demande est invalide." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    private static MachineRequestListItem ToListItem(MachineRequestRecord request) => new(
        request.RequestId,
        request.CreatedAt,
        request.Status,
        request.Client.FirstName,
        request.Client.LastName,
        request.Client.Company,
        request.Client.Email,
        request.Client.Phone,
        request.Machine.MachineName,
        request.Machine.Manufacturer,
        request.Machine.Model,
        request.Machine.SerialNumber,
        request.Machine.Description,
        request.Documents.Count,
        request.Pricing.TotalPages,
        request.Pricing.PreparationTotal,
        RequestKind(request.RequestKind),
        request.CompanyId,
        request.RequestedByUserId,
        request.IsArchived,
        request.ArchivedAtUtc,
        request.ArchivedByUserId);

    private static MachineRequestDetail ToDetail(MachineRequestRecord request, MachineRequestPaymentResult? payment) => new(
        request.RequestId,
        request.CreatedAt,
        request.Status,
        request.Client,
        request.Machine,
        request.Documents.Select(document => new MachineRequestDocumentDetail(
            CreateDocumentId(document.BlobName),
            document.OriginalName,
            document.Size,
            document.PageCount)).ToArray(),
        request.Pricing,
        RequestKind(request.RequestKind),
        request.CompanyId,
        request.RequestedByUserId,
        payment?.PreparationStatus ?? "pending",
        payment?.ReadyAtUtc,
        payment?.ReadyByUserId,
        request.Payment is null ? null : new MachineRequestPaymentAdminDetail(
            request.Payment.PaymentRequestId,
            payment?.Status ?? "unknown",
            payment?.Amount ?? request.Payment.AuthorizedAmountCents / 100m,
            payment?.Currency ?? request.Payment.Currency,
            request.Payment.AuthorizationInsufficient,
            payment?.InitialAuthorizationAmount,
            payment?.ProvisioningStage ?? "awaitingAcceptance",
            payment?.CompanyId,
            payment?.MachineId,
            request.Pricing.PreparationTotal,
            payment?.ActivatedAtUtc,
            payment?.FirstPeriodEndUtc,
            payment?.ServiceAmountCents),
        request.IsArchived,
        request.ArchivedAtUtc,
        request.ArchivedByUserId);

    private static string CreateDocumentId(string blobName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blobName)))[..24].ToLowerInvariant();

    private static string RequestKind(MachineRequestKind kind) => kind switch
    {
        MachineRequestKind.InitialMachine => "initialMachine",
        MachineRequestKind.AdditionalMachine => "additionalMachine",
        MachineRequestKind.AdditionalDocuments => "additionalDocuments",
        _ => throw new InvalidOperationException("Type de demande machine inconnu.")
    };
}

public sealed record MachineRequestListItem(
    string RequestId,
    DateTimeOffset CreatedAt,
    string Status,
    string FirstName,
    string LastName,
    string Company,
    string Email,
    string Phone,
    string MachineName,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    string? Description,
    int DocumentCount,
    int TotalPages,
    decimal PreparationTotal,
    string RequestKind,
    Guid? CompanyId,
    Guid? RequestedByUserId,
    bool IsArchived,
    DateTimeOffset? ArchivedAtUtc,
    Guid? ArchivedByUserId);

public sealed record MachineRequestDocumentDetail(string DocumentId, string OriginalName, long Size, int PageCount);

public sealed record MachineRequestDetail(
    string RequestId,
    DateTimeOffset CreatedAt,
    string Status,
    MachineRequestClient Client,
    MachineRequestMachine Machine,
    IReadOnlyList<MachineRequestDocumentDetail> Documents,
    MachineRequestPricing Pricing,
    string RequestKind,
    Guid? CompanyId,
    Guid? RequestedByUserId,
    string PreparationStatus,
    DateTime? ReadyAtUtc,
    Guid? ReadyByUserId,
    MachineRequestPaymentAdminDetail? Payment,
    bool IsArchived,
    DateTimeOffset? ArchivedAtUtc,
    Guid? ArchivedByUserId);

public sealed record MachineRequestPaymentAdminDetail(
    Guid PaymentRequestId,
    string Status,
    decimal Amount,
    string Currency,
    bool AuthorizationInsufficient,
    decimal? InitialAuthorizationAmount,
    string ProvisioningStage,
    Guid? CompanyId,
    Guid? MachineId,
    decimal PreparationAmount,
    DateTime? ActivatedAtUtc,
    DateTime? FirstPeriodEndUtc,
    int? ServiceAmountCents);

public sealed record MachineRequestStatusUpdate(string Status);
public sealed record MachineRequestArchiveUpdate(bool IsArchived);
public sealed record MachineRequestBusinessEntitiesUpdate(Guid CompanyId, Guid MachineId);
