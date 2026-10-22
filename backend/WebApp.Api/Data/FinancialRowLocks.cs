using Microsoft.EntityFrameworkCore;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Data;

/// <summary>
/// Provider-specific pessimistic row locks used by financial transactions.
/// Callers must already own a transaction; PostgreSQL's FOR UPDATE lock is held until commit.
/// </summary>
internal static class FinancialRowLocks
{
    internal const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    internal static bool IsSupported(DiagLinkDbContext db) =>
        db.Database.IsSqlServer() || db.Database.IsNpgsql()
        || string.Equals(db.Database.ProviderName, SqliteProvider, StringComparison.Ordinal);

    internal static FormattableString PostgreSqlCompany(Guid companyId) =>
        $"SELECT * FROM \"dbo\".\"Companies\" WHERE \"Id\" = {companyId} FOR UPDATE";

    internal static FormattableString PostgreSqlMachine(Guid machineId) =>
        $"SELECT * FROM \"dbo\".\"Machines\" WHERE \"Id\" = {machineId} FOR UPDATE";

    internal static FormattableString PostgreSqlWallet(Guid companyId) =>
        $"SELECT * FROM \"dbo\".\"CompanyWallets\" WHERE \"CompanyId\" = {companyId} FOR UPDATE";

    internal static FormattableString SqlServerCompany(Guid companyId) =>
        $"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {companyId}";

    internal static FormattableString SqlServerMachine(Guid machineId) =>
        $"SELECT * FROM [dbo].[Machines] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {machineId}";

    internal static FormattableString SqlServerWallet(Guid companyId) =>
        $"SELECT * FROM [dbo].[CompanyWallets] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyId] = {companyId}";

    internal static async Task<Company?> CompanyAsync(
        DiagLinkDbContext db,
        Guid companyId,
        CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            return (await db.Companies
                .FromSqlInterpolated(SqlServerCompany(companyId))
                .ToListAsync(ct)).SingleOrDefault();

        if (db.Database.IsNpgsql())
            return (await db.Companies.FromSqlInterpolated(PostgreSqlCompany(companyId)).ToListAsync(ct)).SingleOrDefault();

        return await db.Companies.SingleOrDefaultAsync(company => company.Id == companyId, ct);
    }

    internal static async Task<Machine?> MachineAsync(
        DiagLinkDbContext db,
        Guid machineId,
        CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            return (await db.Machines
                .FromSqlInterpolated(SqlServerMachine(machineId))
                .ToListAsync(ct)).SingleOrDefault();

        if (db.Database.IsNpgsql())
            return (await db.Machines.FromSqlInterpolated(PostgreSqlMachine(machineId)).ToListAsync(ct)).SingleOrDefault();

        return await db.Machines.SingleOrDefaultAsync(machine => machine.Id == machineId, ct);
    }

    internal static async Task<CompanyWallet?> WalletAsync(
        DiagLinkDbContext db,
        Guid companyId,
        CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
            return (await db.CompanyWallets
                .FromSqlInterpolated(SqlServerWallet(companyId))
                .ToListAsync(ct)).SingleOrDefault();

        if (db.Database.IsNpgsql())
            return (await db.CompanyWallets.FromSqlInterpolated(PostgreSqlWallet(companyId)).ToListAsync(ct)).SingleOrDefault();

        return await db.CompanyWallets.SingleOrDefaultAsync(wallet => wallet.CompanyId == companyId, ct);
    }
}
