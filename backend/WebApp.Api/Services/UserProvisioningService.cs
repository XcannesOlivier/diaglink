using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public enum UserProvisioningErrorKind
{
    InvalidEmail,
    DuplicateEmail,
    CompanyNotFound,
    CompanyNotActive,
    MissingRequiredField,
    InvalidRole,
}

public record CreateTechnicianOutcome
{
    public bool Success { get; init; }
    public CompanyUserDto? User { get; init; }
    public UserProvisioningErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateTechnicianOutcome Ok(CompanyUserDto user) => new() { Success = true, User = user };
    public static CreateTechnicianOutcome Error(UserProvisioningErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

/// <summary>Outcome kinds for <see cref="UserProvisioningService.DeactivateUserAsync"/>.</summary>
public enum UserDeactivationErrorKind
{
    UserNotFound,
    CannotDeleteSuperAdmin,
    CannotDeleteSelf,
}

public record DeactivateUserOutcome
{
    public bool Success { get; init; }
    public UserDeactivationErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeactivateUserOutcome Ok() => new() { Success = true };
    public static DeactivateUserOutcome Error(UserDeactivationErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

public enum UserProfileUpdateErrorKind
{
    UserNotFound,
    MissingRequiredField,
    FieldTooLong,
}

public record UpdateUserProfileOutcome
{
    public bool Success { get; init; }
    public CompanyUserDto? User { get; init; }
    public UserProfileUpdateErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateUserProfileOutcome Ok(CompanyUserDto user) => new() { Success = true, User = user };
    public static UpdateUserProfileOutcome Error(UserProfileUpdateErrorKind kind, string message) => new() { ErrorKind = kind, ErrorMessage = message };
}

/// <summary>
/// Creates technician accounts in dbo.Users. CompanyId is always supplied by the caller (Program.cs
/// resolves it from either the company_admin's own claim or a super-admin-supplied route parameter —
/// never from request body). Role is always forced to Technician; no other role can be created here.
/// </summary>
public class UserProvisioningService
{
    private const int MaxEmailLength = 320;
    private const int MaxNameLength = 100;
    private const int MaxPhoneNumberLength = 30;

    private readonly DiagLinkDbContext _db;

    public UserProvisioningService(DiagLinkDbContext db)
    {
        _db = db;
    }

    /// <summary>Used by POST /api/company/users (company_admin) — the caller already belongs to
    /// companyId, so no existence/active check is performed here.</summary>
    public Task<CreateTechnicianOutcome> CreateTechnicianAsync(Guid companyId, CreateTechnicianRequest request, CancellationToken cancellationToken)
        => CreateTechnicianCoreAsync(companyId, request, validateCompany: false, cancellationToken);

    /// <summary>Used by POST /api/companies/{companyId}/users (diaglink_super_admin) — the target
    /// company must be validated since the super-admin can target any company id.</summary>
    public Task<CreateTechnicianOutcome> CreateTechnicianForCompanyAsync(Guid companyId, CreateTechnicianRequest request, CancellationToken cancellationToken)
        => CreateTechnicianCoreAsync(companyId, request, validateCompany: true, cancellationToken);

    /// <summary>Updates only the three editable profile fields. The company-scoped lookup is the
    /// tenant boundary for both company admins and super admins.</summary>
    public async Task<UpdateUserProfileOutcome> UpdateUserProfileAsync(
        Guid companyId,
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var firstName = ValidateProfileField(request.FirstName, "Le prénom", MaxNameLength);
        if (firstName.ErrorKind is not null) return UpdateUserProfileOutcome.Error(firstName.ErrorKind.Value, firstName.ErrorMessage!);

        var lastName = ValidateProfileField(request.LastName, "Le nom", MaxNameLength);
        if (lastName.ErrorKind is not null) return UpdateUserProfileOutcome.Error(lastName.ErrorKind.Value, lastName.ErrorMessage!);

        var phoneNumber = ValidateProfileField(request.PhoneNumber, "Le téléphone", MaxPhoneNumberLength);
        if (phoneNumber.ErrorKind is not null) return UpdateUserProfileOutcome.Error(phoneNumber.ErrorKind.Value, phoneNumber.ErrorMessage!);

        var user = await _db.Users.FirstOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.CompanyId == companyId,
            cancellationToken);
        if (user is null)
        {
            return UpdateUserProfileOutcome.Error(UserProfileUpdateErrorKind.UserNotFound, "Utilisateur introuvable.");
        }

        user.FirstName = firstName.NormalizedValue;
        user.LastName = lastName.NormalizedValue;
        user.PhoneNumber = phoneNumber.NormalizedValue;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return UpdateUserProfileOutcome.Ok(ToCompanyUserDto(user));
    }

    private async Task<CreateTechnicianOutcome> CreateTechnicianCoreAsync(Guid companyId, CreateTechnicianRequest request, bool validateCompany, CancellationToken cancellationToken)
    {
        if (validateCompany)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
            if (company is null)
            {
                return CreateTechnicianOutcome.Error(UserProvisioningErrorKind.CompanyNotFound, "Entreprise introuvable.");
            }

            if (!string.Equals(company.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTechnicianOutcome.Error(UserProvisioningErrorKind.CompanyNotActive, "L'entreprise ciblée n'est pas active.");
            }
        }

        var emailValidation = ValidateEmail(request.Email);
        if (emailValidation.ErrorKind is not null)
        {
            return CreateTechnicianOutcome.Error(emailValidation.ErrorKind.Value, emailValidation.ErrorMessage!);
        }

        var firstNameValidation = ValidateRequiredField(request.FirstName, "Le prénom");
        if (firstNameValidation.ErrorKind is not null)
        {
            return CreateTechnicianOutcome.Error(firstNameValidation.ErrorKind.Value, firstNameValidation.ErrorMessage!);
        }

        var lastNameValidation = ValidateRequiredField(request.LastName, "Le nom");
        if (lastNameValidation.ErrorKind is not null)
        {
            return CreateTechnicianOutcome.Error(lastNameValidation.ErrorKind.Value, lastNameValidation.ErrorMessage!);
        }

        var phoneValidation = ValidateRequiredField(request.PhoneNumber, "Le téléphone");
        if (phoneValidation.ErrorKind is not null)
        {
            return CreateTechnicianOutcome.Error(phoneValidation.ErrorKind.Value, phoneValidation.ErrorMessage!);
        }

        // Never trust a client-supplied role beyond this allow-list — diaglink_super_admin can
        // only ever be granted directly in SQL, never through this endpoint.
        var roleValidation = ValidateRole(request.Role);
        if (roleValidation.ErrorKind is not null)
        {
            return CreateTechnicianOutcome.Error(roleValidation.ErrorKind.Value, roleValidation.ErrorMessage!);
        }

        var normalizedEmail = emailValidation.NormalizedValue!;
        if (await IsDuplicateEmailAsync(normalizedEmail, cancellationToken))
        {
            return CreateTechnicianOutcome.Error(UserProvisioningErrorKind.DuplicateEmail, "Un utilisateur existe déjà avec cet email.");
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            EntraObjectId = null,
            CompanyId = companyId,
            Email = normalizedEmail,
            FirstName = firstNameValidation.NormalizedValue,
            LastName = lastNameValidation.NormalizedValue,
            PhoneNumber = phoneValidation.NormalizedValue,
            Role = roleValidation.NormalizedValue!,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return CreateTechnicianOutcome.Error(UserProvisioningErrorKind.DuplicateEmail, "Un utilisateur existe déjà avec cet email.");
        }

        return CreateTechnicianOutcome.Ok(ToCompanyUserDto(user));
    }

    /// <summary>
    /// Soft-deletes a user by setting Status to "inactive" — mirrors the app's existing convention
    /// (DiagLinkUserLookupService already treats Status != "active" as blocked/no-access), instead of a
    /// physical DELETE which would orphan MachineAccess/Conversation rows that reference this Guid.
    /// companyId scopes the lookup so a company_admin can never reach a user outside their own
    /// company, and a super-admin's route-supplied companyId still confirms the expected tenant.
    /// </summary>
    public async Task<DeactivateUserOutcome> DeactivateUserAsync(Guid companyId, Guid userId, Guid? callerUserId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, cancellationToken);
        if (user is null)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.UserNotFound, "Utilisateur introuvable.");
        }

        // diaglink_super_admin can only ever be deactivated directly in SQL, never through this endpoint.
        if (string.Equals(user.Role, DiagLinkRoles.SuperAdmin, StringComparison.Ordinal))
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSuperAdmin, "Impossible de supprimer un super administrateur DiagLink.");
        }

        if (callerUserId.HasValue && callerUserId.Value == userId)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSelf, "Vous ne pouvez pas supprimer votre propre compte.");
        }

        if (!string.Equals(user.Status, "inactive", StringComparison.OrdinalIgnoreCase))
        {
            user.Status = "inactive";
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return DeactivateUserOutcome.Ok();
    }

    /// <summary>Reverses <see cref="DeactivateUserAsync"/> — same guard rails (scoping, never
    /// diaglink_super_admin, never self) since re-granting access is just as sensitive as revoking it.</summary>
    public async Task<DeactivateUserOutcome> ReactivateUserAsync(Guid companyId, Guid userId, Guid? callerUserId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, cancellationToken);
        if (user is null)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.UserNotFound, "Utilisateur introuvable.");
        }

        if (string.Equals(user.Role, DiagLinkRoles.SuperAdmin, StringComparison.Ordinal))
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSuperAdmin, "Impossible de réactiver un super administrateur DiagLink.");
        }

        if (callerUserId.HasValue && callerUserId.Value == userId)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSelf, "Vous ne pouvez pas réactiver votre propre compte.");
        }

        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            user.Status = "active";
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return DeactivateUserOutcome.Ok();
    }

    /// <summary>
    /// Permanent, irreversible deletion of dbo.Users row — distinct from <see cref="DeactivateUserAsync"/>.
    /// Same guard rails (scoping, never diaglink_super_admin, never self). Cleans up dbo.UserMachines
    /// (UserId carries no SQL FK, so it would silently orphan otherwise) and chat.UserSessions/LoginCodes
    /// (a still-valid session/OTP for a deleted user must not keep authenticating — validation there only
    /// checks TokenHash/expiry, never re-verifies the user still exists). chat.Conversations/ConversationMessages
    /// are deliberately left untouched: UserObjectId is an opaque history string with no SQL FK to dbo.Users
    /// by design (see Conversation doc comment), so business history survives the account being deleted.
    /// </summary>
    public async Task<DeactivateUserOutcome> PermanentlyDeleteUserAsync(Guid companyId, Guid userId, Guid? callerUserId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, cancellationToken);
        if (user is null)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.UserNotFound, "Utilisateur introuvable.");
        }

        if (string.Equals(user.Role, DiagLinkRoles.SuperAdmin, StringComparison.Ordinal))
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSuperAdmin, "Impossible de supprimer un super administrateur DiagLink.");
        }

        if (callerUserId.HasValue && callerUserId.Value == userId)
        {
            return DeactivateUserOutcome.Error(UserDeactivationErrorKind.CannotDeleteSelf, "Vous ne pouvez pas supprimer votre propre compte.");
        }

        var machineAccess = await _db.UserMachineAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        _db.UserMachineAccess.RemoveRange(machineAccess);

        var sessions = await _db.UserSessions.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        _db.UserSessions.RemoveRange(sessions);

        var loginCodes = await _db.LoginCodes.Where(l => l.UserId == userId).ToListAsync(cancellationToken);
        _db.LoginCodes.RemoveRange(loginCodes);

        _db.Users.Remove(user);

        await _db.SaveChangesAsync(cancellationToken);

        return DeactivateUserOutcome.Ok();
    }

    private Task<bool> IsDuplicateEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);

    private static (string? NormalizedValue, UserProvisioningErrorKind? ErrorKind, string? ErrorMessage) ValidateEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, UserProvisioningErrorKind.InvalidEmail, "L'email est obligatoire.");
        }

        if (trimmed.Length > MaxEmailLength)
        {
            return (null, UserProvisioningErrorKind.InvalidEmail, "L'email est trop long.");
        }

        try
        {
            _ = new MailAddress(trimmed);
        }
        catch (FormatException)
        {
            return (null, UserProvisioningErrorKind.InvalidEmail, "L'email n'est pas valide.");
        }

        return (trimmed, null, null);
    }

    private static (string? NormalizedValue, UserProvisioningErrorKind? ErrorKind, string? ErrorMessage) ValidateRequiredField(string? value, string fieldLabel)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? (null, UserProvisioningErrorKind.MissingRequiredField, $"{fieldLabel} est obligatoire.")
            : (trimmed, null, null);
    }

    private static (string? NormalizedValue, UserProfileUpdateErrorKind? ErrorKind, string? ErrorMessage) ValidateProfileField(
        string? value,
        string fieldLabel,
        int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, UserProfileUpdateErrorKind.MissingRequiredField, $"{fieldLabel} est obligatoire.");
        }

        return trimmed.Length > maxLength
            ? (null, UserProfileUpdateErrorKind.FieldTooLong, $"{fieldLabel} est trop long.")
            : (trimmed, null, null);
    }

    /// <summary>Only technician/company_admin can be created here — diaglink_super_admin is
    /// deliberately excluded from this allow-list regardless of what the client sends.</summary>
    private static (string? NormalizedValue, UserProvisioningErrorKind? ErrorKind, string? ErrorMessage) ValidateRole(string? role)
    {
        var trimmed = role?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, UserProvisioningErrorKind.MissingRequiredField, "Le rôle est obligatoire.");
        }

        if (trimmed != DiagLinkRoles.Technician && trimmed != DiagLinkRoles.CompanyAdmin)
        {
            return (null, UserProvisioningErrorKind.InvalidRole, "Rôle non autorisé.");
        }

        return (trimmed, null, null);
    }

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
