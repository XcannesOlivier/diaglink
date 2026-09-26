namespace WebApp.Api.Models;

public sealed record ContactRequest(
    string? Name,
    string? Company,
    string? Email,
    string? Phone,
    string? Message);

public sealed record ContactResponse(bool Success);