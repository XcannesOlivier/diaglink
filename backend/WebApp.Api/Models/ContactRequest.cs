namespace WebApp.Api.Models;

public sealed record ContactRequest(
    string? Name,
    string? Company,
    string? Email,
    string? Phone,
    string? Message);

public sealed record SupportContactRequest(string? Message, Guid? MachineId);

public sealed record ContactResponse(bool Success);