using System.Text.Json.Serialization;

namespace WebApp.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MachineRequestKind
{
    InitialMachine = 0,
    AdditionalMachine = 1,
    AdditionalDocuments = 2
}

public static class MachineRequestStatuses
{
    public const string Pending = "pending";
    public const string Treated = "treated";
    public const string Rejected = "rejected";

    public static bool IsValid(string status) => status is Pending or Treated or Rejected;
}

public sealed record MachineRequestClient(
    string FirstName,
    string LastName,
    string Company,
    string Email,
    string Phone);

public sealed record MachineRequestMachine(
    string MachineName,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    string? Description);

public sealed record MachineRequestDocument(
    string OriginalName,
    string BlobName,
    long Size,
    int PageCount);

public sealed record MachineRequestPricing(
    int TotalPages,
    int IncludedPages,
    int AdditionalPages,
    decimal BasePreparationPrice,
    decimal AdditionalPagePrice,
    decimal PreparationTotal,
    decimal MonthlySubscriptionPrice);

public sealed record MachineRequestPaymentReference(
    Guid PaymentRequestId,
    int AuthorizedPages,
    long AuthorizedAmountCents,
    string Currency,
    int ReceivedPages,
    long RecalculatedAmountCents,
    int PageDifference,
    long AmountDifferenceCents,
    bool AuthorizationInsufficient);

public sealed record MachineRequestRecord(
    string RequestId,
    DateTimeOffset CreatedAt,
    string Status,
    MachineRequestClient Client,
    MachineRequestMachine Machine,
    IReadOnlyList<MachineRequestDocument> Documents,
    MachineRequestPricing Pricing,
    MachineRequestPaymentReference? Payment = null,
    MachineRequestKind RequestKind = MachineRequestKind.InitialMachine,
    Guid? CompanyId = null,
    Guid? RequestedByUserId = null,
    Guid? TargetMachineId = null,
    bool IsArchived = false,
    DateTimeOffset? ArchivedAtUtc = null,
    Guid? ArchivedByUserId = null);

public sealed record MachineRequestDraft(
    MachineRequestClient Client,
    MachineRequestMachine Machine);

public sealed record MachineRequestDocumentUpload(
    string OriginalName,
    string ContentType,
    long Size,
    int PageCount,
    Stream Content);

public sealed record MachineRequestDocumentDownload(
    string OriginalName,
    string ContentType,
    Stream Content);
