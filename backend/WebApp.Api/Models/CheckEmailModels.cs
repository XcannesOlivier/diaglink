namespace WebApp.Api.Models;

public record CheckEmailRequest
{
    public required string Email { get; init; }
}

public record CheckEmailResponse
{
    public required bool Known { get; init; }
}

public record RequestCodeRequest
{
    public required string Email { get; init; }
}

public record RequestCodeResponse
{
    public required bool Success { get; init; }
}

public record VerifyCodeRequest
{
    public required string Email { get; init; }
    public required string Code { get; init; }
}

public record VerifyCodeResponse
{
    public required bool Success { get; init; }
    public string? SessionToken { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}

public record ValidateSessionRequest
{
    public required string SessionToken { get; init; }
}

public record ValidateSessionResponse
{
    public required bool Valid { get; init; }
    public Guid? UserId { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}