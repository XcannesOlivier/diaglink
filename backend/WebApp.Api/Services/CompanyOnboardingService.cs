using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public enum CompanyOnboardingErrorKind
{
    InvalidCompanyName,
    InvalidFirstName,
    InvalidLastName,
    InvalidPhoneNumber,
    InvalidEmail,
    DuplicateCompanyName,
    DuplicateEmail,
    CompanyNotFound,
    CompanyNotActive,
    TechnicalError,
}

public record CreateCompanyOutcome
{
    public bool Success { get; init; }
    public CompanyDto? Company { get; init; }
    public CompanyOnboardingErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateCompanyOutcome Ok(CompanyDto company) => new() { Success = true, Company = company };
    public static CreateCompanyOutcome Error(CompanyOnboardingErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

public record AddCompanyAdminOutcome
{
    public bool Success { get; init; }
    public CompanyUserDto? Admin { get; init; }
    public CompanyOnboardingErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static AddCompanyAdminOutcome Ok(CompanyUserDto admin) => new() { Success = true, Admin = admin };
    public static AddCompanyAdminOutcome Error(CompanyOnboardingErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

public record OnboardCompanyOutcome
{
    public bool Success { get; init; }
    public CompanyOnboardingResultDto? Result { get; init; }
    public CompanyOnboardingErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static OnboardCompanyOutcome Ok(CompanyOnboardingResultDto result) => new() { Success = true, Result = result };
    public static OnboardCompanyOutcome Error(CompanyOnboardingErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

/// <summary>
/// Onboards new DiagLink companies and their first company_admin. Writes to dbo.Companies (owned by
/// this app) and dbo.Users (pre-existing, excluded from migrations — see DiagLinkDbContext) — the User
/// entity's mapped columns (Id, EntraObjectId, CompanyId, Email, Role, Status, CreatedAt, UpdatedAt)
/// are the only ones this app knows about and are already relied upon everywhere else for auth, so a
/// row created with all of them populated matches every other read path (OTP, session, claims lookup).
/// Never accepts Role/Status/CompanyId/Id from the caller — always forced server-side.
/// </summary>
public class CompanyOnboardingService
{
    private const int MaxCompanyNameLength = 200;
    private const int MaxEmailLength = 320;

    private readonly DiagLinkDbContext _db;
    private readonly ILogger<CompanyOnboardingService> _logger;

    public CompanyOnboardingService(DiagLinkDbContext db, ILogger<CompanyOnboardingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CreateCompanyOutcome> CreateCompanyAsync(string? name, CancellationToken cancellationToken)
    {
        var nameValidation = ValidateCompanyName(name);
        if (nameValidation.ErrorKind is not null)
        {
            return CreateCompanyOutcome.Error(nameValidation.ErrorKind.Value, nameValidation.ErrorMessage!);
        }

        var normalizedName = nameValidation.NormalizedValue!;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.Companies.Add(company);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create company.");
            return CreateCompanyOutcome.Error(CompanyOnboardingErrorKind.TechnicalError, "Une erreur technique est survenue.");
        }

        return CreateCompanyOutcome.Ok(ToCompanyDto(company));
    }

    public async Task<AddCompanyAdminOutcome> AddCompanyAdminAsync(Guid companyId, string? email, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null)
        {
            return AddCompanyAdminOutcome.Error(CompanyOnboardingErrorKind.CompanyNotFound, "Entreprise introuvable.");
        }

        if (!string.Equals(company.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return AddCompanyAdminOutcome.Error(CompanyOnboardingErrorKind.CompanyNotActive, "L'entreprise ciblée n'est pas active.");
        }

        var emailValidation = ValidateEmail(email);
        if (emailValidation.ErrorKind is not null)
        {
            return AddCompanyAdminOutcome.Error(emailValidation.ErrorKind.Value, emailValidation.ErrorMessage!);
        }

        var normalizedEmail = emailValidation.NormalizedValue!;
        if (await IsDuplicateEmailAsync(normalizedEmail, cancellationToken))
        {
            return AddCompanyAdminOutcome.Error(CompanyOnboardingErrorKind.DuplicateEmail, "Cette adresse e-mail est déjà associée à un utilisateur.");
        }

        var admin = NewCompanyAdmin(companyId, normalizedEmail);
        _db.Users.Add(admin);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to add company admin for company {CompanyId}.", companyId);
            return AddCompanyAdminOutcome.Error(CompanyOnboardingErrorKind.TechnicalError, "Une erreur technique est survenue.");
        }

        return AddCompanyAdminOutcome.Ok(ToCompanyUserDto(admin));
    }

    public async Task<OnboardCompanyOutcome> OnboardCompanyAsync(string? companyName, string? adminEmail, string? firstName, string? lastName, string? phoneNumber, CancellationToken cancellationToken)
    {
        var nameValidation = ValidateCompanyName(companyName);
        if (nameValidation.ErrorKind is not null)
        {
            return OnboardCompanyOutcome.Error(nameValidation.ErrorKind.Value, nameValidation.ErrorMessage!);
        }

        var emailValidation = ValidateEmail(adminEmail);
        if (emailValidation.ErrorKind is not null)
        {
            return OnboardCompanyOutcome.Error(emailValidation.ErrorKind.Value, emailValidation.ErrorMessage!);
        }

        var firstNameValidation = ValidateContactName(firstName, MaxFirstNameLength, CompanyOnboardingErrorKind.InvalidFirstName, "Le prénom du responsable est obligatoire.");
        if (firstNameValidation.ErrorKind is not null)
        {
            return OnboardCompanyOutcome.Error(firstNameValidation.ErrorKind.Value, firstNameValidation.ErrorMessage!);
        }

        var lastNameValidation = ValidateContactName(lastName, MaxLastNameLength, CompanyOnboardingErrorKind.InvalidLastName, "Le nom du responsable est obligatoire.");
        if (lastNameValidation.ErrorKind is not null)
        {
            return OnboardCompanyOutcome.Error(lastNameValidation.ErrorKind.Value, lastNameValidation.ErrorMessage!);
        }

        var phoneValidation = ValidatePhoneNumber(phoneNumber);
        if (phoneValidation.ErrorKind is not null)
        {
            return OnboardCompanyOutcome.Error(phoneValidation.ErrorKind.Value, phoneValidation.ErrorMessage!);
        }

        var normalizedName = nameValidation.NormalizedValue!;
        var normalizedEmail = emailValidation.NormalizedValue!;

        if (await IsDuplicateEmailAsync(normalizedEmail, cancellationToken))
        {
            return OnboardCompanyOutcome.Error(CompanyOnboardingErrorKind.DuplicateEmail, "Cette adresse e-mail est déjà associée à un utilisateur.");
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var admin = NewCompanyAdmin(company.Id, normalizedEmail);
        // assign contact fields (already validated and trimmed)
        admin.FirstName = firstNameValidation.NormalizedValue;
        admin.LastName = lastNameValidation.NormalizedValue;
        admin.PhoneNumber = phoneValidation.NormalizedValue;

        // Use the DbContext execution strategy to run the transactional work as a retriable unit.
        var strategy = _db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    _db.Companies.Add(company);
                    _db.Users.Add(admin);
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw; // rethrow so the outer catch can map to an onboarding outcome
                }
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to onboard company and admin user.");
            return OnboardCompanyOutcome.Error(CompanyOnboardingErrorKind.TechnicalError, "Une erreur technique est survenue.");
        }

        return OnboardCompanyOutcome.Ok(new CompanyOnboardingResultDto
        {
            Company = ToCompanyDto(company),
            Admin = ToCompanyUserDto(admin),
        });
    }

    private const int MaxFirstNameLength = 100;
    private const int MaxLastNameLength = 100;
    private const int MaxPhoneLength = 30;

    private static (string? NormalizedValue, CompanyOnboardingErrorKind? ErrorKind, string? ErrorMessage) ValidateContactName(string? value, int maxLen, CompanyOnboardingErrorKind kind, string requiredMessage)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, kind, requiredMessage);
        }

        if (trimmed.Length > maxLen)
        {
            return (null, kind, $"La longueur maximale est de {maxLen} caractères.");
        }

        return (trimmed, null, null);
    }

    private static (string? NormalizedValue, CompanyOnboardingErrorKind? ErrorKind, string? ErrorMessage) ValidatePhoneNumber(string? phone)
    {
        var trimmed = phone?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, CompanyOnboardingErrorKind.InvalidPhoneNumber, "Le numéro de téléphone est obligatoire.");
        }

        if (trimmed.Length > MaxPhoneLength)
        {
            return (null, CompanyOnboardingErrorKind.InvalidPhoneNumber, $"Le numéro de téléphone dépasse {MaxPhoneLength} caractères.");
        }

        return (trimmed, null, null);
    }

    private static User NewCompanyAdmin(Guid companyId, string normalizedEmail)
    {
        var now = DateTime.UtcNow;
        return new User
        {
            Id = Guid.NewGuid(),
            EntraObjectId = null,
            CompanyId = companyId,
            Email = normalizedEmail,
            Role = DiagLinkRoles.CompanyAdmin,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private Task<bool> IsDuplicateEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);

    private static (string? NormalizedValue, CompanyOnboardingErrorKind? ErrorKind, string? ErrorMessage) ValidateCompanyName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, CompanyOnboardingErrorKind.InvalidCompanyName, "Le nom de l'entreprise est obligatoire.");
        }

        if (trimmed.Length < 2 || trimmed.Length > MaxCompanyNameLength)
        {
            return (null, CompanyOnboardingErrorKind.InvalidCompanyName, $"Le nom de l'entreprise doit contenir entre 2 et {MaxCompanyNameLength} caractères.");
        }

        return (trimmed, null, null);
    }

    private static (string? NormalizedValue, CompanyOnboardingErrorKind? ErrorKind, string? ErrorMessage) ValidateEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, CompanyOnboardingErrorKind.InvalidEmail, "L'email est obligatoire.");
        }

        if (trimmed.Length > MaxEmailLength)
        {
            return (null, CompanyOnboardingErrorKind.InvalidEmail, "L'email est trop long.");
        }

        try
        {
            _ = new MailAddress(trimmed);
        }
        catch (FormatException)
        {
            return (null, CompanyOnboardingErrorKind.InvalidEmail, "L'email n'est pas valide.");
        }

        return (trimmed, null, null);
    }

    private static CompanyDto ToCompanyDto(Company company) => new()
    {
        Id = company.Id.ToString(),
        Name = company.Name,
        Status = company.Status,
    };

    private static CompanyUserDto ToCompanyUserDto(User user) => new()
    {
        Id = user.Id.ToString(),
        Email = user.Email,
        Role = user.Role,
        Status = user.Status,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
    };
}
