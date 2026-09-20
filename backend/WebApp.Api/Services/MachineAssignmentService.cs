using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public enum MachineAssignmentErrorKind
{
    UserNotFound,
    UserNotTechnician,
    MachineNotFound,
}

public record MachineAssignmentOutcome
{
    public bool Success { get; init; }
    public MachineAssignmentErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static MachineAssignmentOutcome Ok() => new() { Success = true };
    public static MachineAssignmentOutcome Error(MachineAssignmentErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

public record UserMachineAccessListOutcome
{
    public bool Success { get; init; }
    public List<UserMachineAccessDto>? Machines { get; init; }
    public MachineAssignmentErrorKind? ErrorKind { get; init; }
    public string? ErrorMessage { get; init; }

    public static UserMachineAccessListOutcome Ok(List<UserMachineAccessDto> machines) => new() { Success = true, Machines = machines };
    public static UserMachineAccessListOutcome Error(MachineAssignmentErrorKind kind, string message) => new() { Success = false, ErrorKind = kind, ErrorMessage = message };
}

/// <summary>
/// Grants/revokes technician access to company machines (chat.UserMachineAccess). Every method takes
/// an already-resolved companyId (from the caller's own claim for company_admin, or a route parameter
/// validated by the caller for diaglink_super_admin) and re-validates that both the target user and
/// every target machine actually belong to that company — never trusts client input to cross that
/// boundary, even implicitly.
/// </summary>
public class MachineAssignmentService
{
    private readonly DiagLinkDbContext _db;

    public MachineAssignmentService(DiagLinkDbContext db)
    {
        _db = db;
    }

    /// <summary>All of the company's machines, each flagged with whether userId currently has access.</summary>
    public async Task<UserMachineAccessListOutcome> GetUserMachineAccessAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        var userCheck = await ValidateTargetTechnicianAsync(companyId, userId, cancellationToken);
        if (userCheck is not null)
        {
            return UserMachineAccessListOutcome.Error(userCheck.Value.Kind, userCheck.Value.Message);
        }

        var machines = await _db.Machines.AsNoTracking()
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);

        var assignedMachineIds = (await _db.UserMachineAccess.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.MachineId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var result = machines.Select(m => new UserMachineAccessDto
        {
            MachineId = m.Id.ToString(),
            Name = m.Name,
            Reference = m.Reference,
            Assigned = assignedMachineIds.Contains(m.Id),
        }).ToList();

        return UserMachineAccessListOutcome.Ok(result);
    }

    /// <summary>Idempotent: succeeds even if the access already existed.</summary>
    public async Task<MachineAssignmentOutcome> AssignMachineAsync(Guid companyId, Guid userId, Guid machineId, CancellationToken cancellationToken)
    {
        var userCheck = await ValidateTargetTechnicianAsync(companyId, userId, cancellationToken);
        if (userCheck is not null)
        {
            return MachineAssignmentOutcome.Error(userCheck.Value.Kind, userCheck.Value.Message);
        }

        if (!await _db.Machines.AsNoTracking().AnyAsync(m => m.Id == machineId && m.CompanyId == companyId, cancellationToken))
        {
            return MachineAssignmentOutcome.Error(MachineAssignmentErrorKind.MachineNotFound, "Machine introuvable.");
        }

        var alreadyAssigned = await _db.UserMachineAccess.AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.MachineId == machineId, cancellationToken);

        if (!alreadyAssigned)
        {
            _db.UserMachineAccess.Add(new UserMachineAccess
            {
                UserId = userId,
                MachineId = machineId,
                CreatedAtUtc = DateTime.UtcNow,
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert of the same (UserId, MachineId) pair — already assigned, treat as success.
            }
        }

        return MachineAssignmentOutcome.Ok();
    }

    /// <summary>Idempotent: succeeds even if the access didn't exist.</summary>
    public async Task<MachineAssignmentOutcome> UnassignMachineAsync(Guid companyId, Guid userId, Guid machineId, CancellationToken cancellationToken)
    {
        var userCheck = await ValidateTargetTechnicianAsync(companyId, userId, cancellationToken);
        if (userCheck is not null)
        {
            return MachineAssignmentOutcome.Error(userCheck.Value.Kind, userCheck.Value.Message);
        }

        if (!await _db.Machines.AsNoTracking().AnyAsync(m => m.Id == machineId && m.CompanyId == companyId, cancellationToken))
        {
            return MachineAssignmentOutcome.Error(MachineAssignmentErrorKind.MachineNotFound, "Machine introuvable.");
        }

        var existing = await _db.UserMachineAccess.FirstOrDefaultAsync(a => a.UserId == userId && a.MachineId == machineId, cancellationToken);
        if (existing is not null)
        {
            _db.UserMachineAccess.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return MachineAssignmentOutcome.Ok();
    }

    /// <summary>
    /// Replaces the target user's entire machine access set in one transaction — either every
    /// requested machine id is valid (exists and belongs to companyId) and the full add/remove diff
    /// is committed, or nothing changes at all.
    /// </summary>
    public async Task<MachineAssignmentOutcome> ReplaceUserMachineAccessAsync(Guid companyId, Guid userId, List<string> machineIds, CancellationToken cancellationToken)
    {
        var userCheck = await ValidateTargetTechnicianAsync(companyId, userId, cancellationToken);
        if (userCheck is not null)
        {
            return MachineAssignmentOutcome.Error(userCheck.Value.Kind, userCheck.Value.Message);
        }

        var parsedIds = new List<Guid>();
        foreach (var rawId in machineIds)
        {
            if (!Guid.TryParse(rawId, out var parsed))
            {
                return MachineAssignmentOutcome.Error(MachineAssignmentErrorKind.MachineNotFound, "Identifiant de machine invalide.");
            }
            parsedIds.Add(parsed);
        }

        var requestedIds = parsedIds.ToHashSet();

        var validCompanyMachineIds = (await _db.Machines.AsNoTracking()
            .Where(m => m.CompanyId == companyId && requestedIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        if (validCompanyMachineIds.Count != requestedIds.Count)
        {
            return MachineAssignmentOutcome.Error(MachineAssignmentErrorKind.MachineNotFound, "Une ou plusieurs machines n'appartiennent pas à votre entreprise.");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existingAccess = await _db.UserMachineAccess.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
                var existingMachineIds = existingAccess.Select(a => a.MachineId).ToHashSet();

                var toRemove = existingAccess.Where(a => !requestedIds.Contains(a.MachineId));
                _db.UserMachineAccess.RemoveRange(toRemove);

                var toAdd = requestedIds.Where(id => !existingMachineIds.Contains(id));
                foreach (var machineId in toAdd)
                {
                    _db.UserMachineAccess.Add(new UserMachineAccess
                    {
                        UserId = userId,
                        MachineId = machineId,
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return MachineAssignmentOutcome.Ok();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);

                return MachineAssignmentOutcome.Error(MachineAssignmentErrorKind.MachineNotFound, "Échec de la mise à jour des accès machines.");
            }
        });
    }

    /// <summary>Null when the user exists, belongs to companyId, and is a technician; otherwise the error to return.</summary>
    private async Task<(MachineAssignmentErrorKind Kind, string Message)?> ValidateTargetTechnicianAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.CompanyId != companyId)
        {
            return (MachineAssignmentErrorKind.UserNotFound, "Utilisateur introuvable.");
        }

        if (!string.Equals(user.Role, DiagLinkRoles.Technician, StringComparison.Ordinal))
        {
            return (MachineAssignmentErrorKind.UserNotTechnician, "L'utilisateur ciblé n'est pas un technicien.");
        }

        return null;
    }
}
