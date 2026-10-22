using System.Data;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WebApp.Api.Data;

const string ExpectedHost = "127.0.0.1";
const int MigrationPort = 15432;
const int ReadOnlyTestPort = 15434;
const int SuperAdminPort = 15434;
const string ExpectedDatabase = "diaglink";
const string ExpectedUser = "diaglink_app";
const string PasswordVariable = "DIAGLINK_PROD_DB_PASSWORD";
const string Migration = "20261020002202_InitialPostgreSql";
const string AdminEmailVariable = "DIAGLINK_SUPER_ADMIN_EMAIL";
const string AdminFirstNameVariable = "DIAGLINK_SUPER_ADMIN_FIRST_NAME";
const string AdminLastNameVariable = "DIAGLINK_SUPER_ADMIN_LAST_NAME";
const string AdminEntraObjectIdVariable = "DIAGLINK_SUPER_ADMIN_ENTRA_OBJECT_ID";

var applyInitial = args is ["apply-initial", "--confirm-production-migration"];
var testReadOnly = args is ["test-read-only", "--confirm-production-target"];
var createSuperAdmin = args is ["create-super-admin", "--confirm-production-write"];
if (!applyInitial && !testReadOnly && !createSuperAdmin)
{
    throw new InvalidOperationException(
        "Safety stop: use one of the explicitly supported production commands.");
}

var expectedPort = testReadOnly
    ? ReadOnlyTestPort
    : createSuperAdmin
        ? SuperAdminPort
        : MigrationPort;

string? adminEmail = null;
string? adminFirstName = null;
string? adminLastName = null;
string? adminEntraObjectId = null;
if (createSuperAdmin)
{
    adminEmail = RequiredAdminValue(AdminEmailVariable, 320).ToLowerInvariant();
    if (!MailAddress.TryCreate(adminEmail, out var parsedEmail)
        || !string.Equals(parsedEmail.Address, adminEmail, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The super-administrator email address is invalid.");
    }

    adminFirstName = RequiredAdminValue(AdminFirstNameVariable, 100);
    adminLastName = RequiredAdminValue(AdminLastNameVariable, 100);
    adminEntraObjectId = Environment.GetEnvironmentVariable(AdminEntraObjectIdVariable)?.Trim();
    if (adminEntraObjectId?.Length > 100)
    {
        throw new InvalidOperationException("The optional Entra object identifier is too long.");
    }

    ClearAdminEnvironment();
}

var password = Environment.GetEnvironmentVariable(PasswordVariable);
if (string.IsNullOrWhiteSpace(password))
{
    throw new InvalidOperationException($"{PasswordVariable} must be set in this process.");
}

var connectionBuilder = new NpgsqlConnectionStringBuilder
{
    Host = ExpectedHost,
    Port = expectedPort,
    Database = ExpectedDatabase,
    Username = ExpectedUser,
    Password = password,
    MaxPoolSize = 10,
    MinPoolSize = 0,
    Timeout = 15,
    CommandTimeout = 30,
    KeepAlive = 30,
    SslMode = SslMode.Disable,
    ApplicationName = testReadOnly
        ? "DiagLinkReadOnlyConnectivityCheck"
        : createSuperAdmin
            ? "DiagLinkSuperAdminBootstrap"
            : "DiagLinkInitialProductionMigration",
    Options = testReadOnly ? "-c default_transaction_read_only=on" : null
};

if (!string.Equals(connectionBuilder.Host, ExpectedHost, StringComparison.Ordinal)
    || connectionBuilder.Port != expectedPort
    || !string.Equals(connectionBuilder.Database, ExpectedDatabase, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Safety stop: invalid PostgreSQL production target.");
}

password = null;
Environment.SetEnvironmentVariable(PasswordVariable, null);

try
{
    await using var verificationConnection = new NpgsqlConnection(connectionBuilder.ConnectionString);
    await verificationConnection.OpenAsync();

    var actualDatabase = await ScalarAsync<string>(
        verificationConnection,
        "SELECT current_database();");
    if (!string.Equals(actualDatabase, ExpectedDatabase, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Safety stop: expected '{ExpectedDatabase}', connected to an unexpected database.");
    }

    if (testReadOnly)
    {
        var selectOne = await ScalarAsync<int>(verificationConnection, "SELECT 1;");
        if (selectOne != 1)
        {
            throw new InvalidOperationException("Read-only connectivity check failed: SELECT 1 returned an unexpected value.");
        }

        var readOnlyHistoryExists = await ScalarAsync<bool>(
            verificationConnection,
            "SELECT to_regclass('chat.\"__EFMigrationsHistory\"') IS NOT NULL;");
        if (!readOnlyHistoryExists)
        {
            throw new InvalidOperationException("Read-only connectivity check failed: migration history is missing.");
        }

        var migrationRows = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int
            FROM chat."__EFMigrationsHistory"
            WHERE "MigrationId"='20261020002202_InitialPostgreSql';
            """);
        if (migrationRows != 1)
        {
            throw new InvalidOperationException(
                $"Read-only connectivity check failed: expected one '{Migration}' history row, found {migrationRows}.");
        }

        Console.WriteLine(
            $"PostgreSQL read-only connectivity validated: " +
            $"Database={ExpectedDatabase}; Endpoint={ExpectedHost}:{ReadOnlyTestPort}; " +
            $"SELECT 1=1; Migration={Migration}.");
        return;
    }

    if (createSuperAdmin)
    {
        try
        {
            await CreateSuperAdminAsync(
                verificationConnection,
                adminEmail!,
                adminFirstName!,
                adminLastName!,
                adminEntraObjectId);
        }
        catch (PostgresException ex)
        {
            throw new InvalidOperationException(
                $"PostgreSQL rejected the super-administrator transaction (SQLSTATE {ex.SqlState}).");
        }

        Console.WriteLine(
            "PostgreSQL super-administrator creation validated: " +
            "Database=diaglink; Endpoint=127.0.0.1:15434; " +
            "Users=1; Companies=1; Role=diaglink_super_admin; Status=active; OTP=enabled.");
        return;
    }

    var applicationTableCount = await ScalarAsync<int>(verificationConnection, """
        SELECT count(*)::int
        FROM information_schema.tables
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
          AND table_schema NOT LIKE 'pg_toast%'
          AND table_type = 'BASE TABLE'
          AND NOT (table_schema = 'chat' AND table_name = '__EFMigrationsHistory');
        """);
    if (applicationTableCount != 0)
    {
        throw new InvalidOperationException(
            $"Safety stop: '{ExpectedDatabase}' already contains {applicationTableCount} application table(s).");
    }

    var historyExists = await ScalarAsync<bool>(
        verificationConnection,
        "SELECT to_regclass('chat.\"__EFMigrationsHistory\"') IS NOT NULL;");
    if (historyExists)
    {
        var historyRows = await ScalarAsync<int>(
            verificationConnection,
            "SELECT count(*)::int FROM chat.\"__EFMigrationsHistory\";");
        if (historyRows != 0)
        {
            throw new InvalidOperationException(
                $"Safety stop: migration history already contains {historyRows} row(s).");
        }
    }

    Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
    Environment.SetEnvironmentVariable("ConnectionStrings__DiagLink", connectionBuilder.ConnectionString);

    var factory = new DiagLinkDbContextFactory();
    await using var context = factory.CreateDbContext([]);
    var knownMigrations = context.Database.GetMigrations().ToArray();
    if (knownMigrations is not [Migration])
    {
        throw new InvalidOperationException(
            $"Safety stop: the PostgreSQL assembly must contain only migration '{Migration}'.");
    }

    var migrator = context.GetService<IMigrator>();
    await migrator.MigrateAsync(Migration);

    var checks = new Dictionary<string, int>
    {
        ["Tables"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM information_schema.tables
            WHERE table_schema IN ('dbo','chat') AND table_type='BASE TABLE'
              AND table_name <> '__EFMigrationsHistory';
            """),
        ["PrimaryKeys"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM information_schema.table_constraints
            WHERE table_schema IN ('dbo','chat') AND table_name <> '__EFMigrationsHistory'
              AND constraint_type='PRIMARY KEY';
            """),
        ["ForeignKeys"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM pg_constraint c
            JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname IN ('dbo','chat') AND c.contype='f';
            """),
        ["CascadeForeignKeys"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM pg_constraint c
            JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname IN ('dbo','chat') AND c.contype='f' AND c.confdeltype='c';
            """),
        ["CheckConstraints"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM pg_constraint c
            JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname IN ('dbo','chat') AND c.contype='c' AND c.conname LIKE 'CK_%';
            """),
        ["UniqueIndexes"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM pg_index i
            JOIN pg_class t ON t.oid=i.indrelid
            JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname IN ('dbo','chat') AND t.relname <> '__EFMigrationsHistory'
              AND i.indisunique AND NOT i.indisprimary;
            """),
        ["CriticalUniqueIndexes"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int
            FROM pg_class i
            JOIN pg_namespace n ON n.oid=i.relnamespace
            WHERE n.nspname='dbo' AND i.relkind='i'
              AND i.relname IN (
                'UX_CreditLedger_CompanyWalletUsage',
                'UX_CreditLedger_MachineIncludedUsage',
                'IX_MachineRequestPayments_AuthorizationEventId',
                'IX_StripeWalletTopUps_ExternalEventId',
                'IX_Users_Email_Normalized',
                'IX_Users_EntraObjectId_NotNull');
            """),
        ["Trigger"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM pg_trigger g
            JOIN pg_class t ON t.oid=g.tgrelid
            JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname='dbo' AND t.relname='CreditLedger'
              AND g.tgname='TR_CreditLedger_TopUpImmutable' AND NOT g.tgisinternal;
            """),
        ["Migration"] = await ScalarAsync<int>(verificationConnection, """
            SELECT count(*)::int FROM chat."__EFMigrationsHistory"
            WHERE "MigrationId"='20261020002202_InitialPostgreSql';
            """)
    };

    var expected = new Dictionary<string, int>
    {
        ["Tables"] = 25,
        ["PrimaryKeys"] = 25,
        ["ForeignKeys"] = 26,
        ["CascadeForeignKeys"] = 5,
        ["CheckConstraints"] = 49,
        ["UniqueIndexes"] = 31,
        ["CriticalUniqueIndexes"] = 6,
        ["Trigger"] = 1,
        ["Migration"] = 1
    };

    foreach (var (name, expectedValue) in expected)
    {
        if (checks[name] != expectedValue)
        {
            throw new InvalidOperationException(
                $"Post-migration validation failed for {name}: expected {expectedValue}, found {checks[name]}.");
        }
    }

    Console.WriteLine(
        "PostgreSQL production schema validated: " +
        string.Join(", ", checks.Select(item => $"{item.Key}={item.Value}")));
}
finally
{
    Environment.SetEnvironmentVariable("ConnectionStrings__DiagLink", null);
    Environment.SetEnvironmentVariable("Database__Provider", null);
    Environment.SetEnvironmentVariable(PasswordVariable, null);
    ClearAdminEnvironment();
    connectionBuilder.Password = string.Empty;
    connectionBuilder.Clear();
    NpgsqlConnection.ClearAllPools();
}

static async Task CreateSuperAdminAsync(
    NpgsqlConnection connection,
    string email,
    string firstName,
    string lastName,
    string? entraObjectId)
{
    var migrationRows = await ScalarAsync<int>(connection, """
        SELECT count(*)::int
        FROM chat."__EFMigrationsHistory"
        WHERE "MigrationId"='20261020002202_InitialPostgreSql';
        """);
    if (migrationRows != 1)
    {
        throw new InvalidOperationException("Safety stop: the approved PostgreSQL migration is not present exactly once.");
    }

    var requiredTables = await ScalarAsync<int>(connection, """
        SELECT count(*)::int
        FROM (VALUES ('dbo', 'Companies'), ('dbo', 'Users'), ('chat', 'LoginCodes')) AS expected(schema_name, table_name)
        WHERE to_regclass(format('%I.%I', schema_name, table_name)) IS NOT NULL;
        """);
    if (requiredTables != 3)
    {
        throw new InvalidOperationException("Safety stop: one or more authentication tables are missing.");
    }

    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
    try
    {
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(443424652760011);", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var existingBootstrapRows = await TransactionScalarAsync<int>(connection, transaction, """
            SELECT (
                (SELECT count(*) FROM dbo."Users") +
                (SELECT count(*) FROM dbo."Companies")
            )::int;
            """);
        if (existingBootstrapRows != 0)
        {
            throw new InvalidOperationException(
                "Safety stop: the initial user/company bootstrap requires both tables to be empty.");
        }

        await using (var duplicateCommand = new NpgsqlCommand("""
            SELECT count(*)::int
            FROM dbo."Users"
            WHERE lower("Email") = lower(@email)
               OR (@entra IS NOT NULL AND "EntraObjectId" = @entra);
            """, connection, transaction))
        {
            duplicateCommand.Parameters.AddWithValue("email", email);
            duplicateCommand.Parameters.AddWithValue(
                "entra",
                NpgsqlTypes.NpgsqlDbType.Text,
                string.IsNullOrWhiteSpace(entraObjectId) ? DBNull.Value : entraObjectId);
            var duplicateCount = Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync());
            if (duplicateCount != 0)
            {
                throw new InvalidOperationException("Safety stop: the requested administrator identity already exists.");
            }
        }

        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var insertCompany = new NpgsqlCommand("""
            INSERT INTO dbo."Companies" ("Id", "Name", "Status", "CreatedAt", "UpdatedAt")
            VALUES (@id, 'DiagLink Administration', 'active', now(), now());
            """, connection, transaction))
        {
            insertCompany.Parameters.AddWithValue("id", companyId);
            if (await insertCompany.ExecuteNonQueryAsync() != 1)
            {
                throw new InvalidOperationException("Company creation did not affect exactly one row.");
            }
        }

        await using (var insertUser = new NpgsqlCommand("""
            INSERT INTO dbo."Users"
                ("Id", "EntraObjectId", "FirstName", "LastName", "PhoneNumber",
                 "CompanyId", "Email", "Role", "Status", "CreatedAt", "UpdatedAt")
            VALUES
                (@id, @entra, @firstName, @lastName, NULL,
                 @companyId, @email, 'diaglink_super_admin', 'active', now(), now());
            """, connection, transaction))
        {
            insertUser.Parameters.AddWithValue("id", userId);
            insertUser.Parameters.AddWithValue("companyId", companyId);
            insertUser.Parameters.AddWithValue("email", email);
            insertUser.Parameters.AddWithValue("firstName", firstName);
            insertUser.Parameters.AddWithValue("lastName", lastName);
            insertUser.Parameters.AddWithValue(
                "entra",
                NpgsqlTypes.NpgsqlDbType.Text,
                string.IsNullOrWhiteSpace(entraObjectId) ? DBNull.Value : entraObjectId);
            if (await insertUser.ExecuteNonQueryAsync() != 1)
            {
                throw new InvalidOperationException("User creation did not affect exactly one row.");
            }
        }

        await using (var verifyCommand = new NpgsqlCommand("""
            SELECT count(*)::int
            FROM dbo."Users" u
            JOIN dbo."Companies" c ON c."Id" = u."CompanyId"
            WHERE u."Id"=@userId AND c."Id"=@companyId
              AND u."Role"='diaglink_super_admin' AND u."Status"='active'
              AND c."Status"='active';
            """, connection, transaction))
        {
            verifyCommand.Parameters.AddWithValue("userId", userId);
            verifyCommand.Parameters.AddWithValue("companyId", companyId);
            if (Convert.ToInt32(await verifyCommand.ExecuteScalarAsync()) != 1)
            {
                throw new InvalidOperationException("Super-administrator verification failed before commit.");
            }
        }

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection);
    var result = await command.ExecuteScalarAsync();
    if (result is null or DBNull)
    {
        throw new InvalidOperationException("A PostgreSQL validation query returned no value.");
    }

    return (T)Convert.ChangeType(result, typeof(T));
}

static async Task<T> TransactionScalarAsync<T>(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    var result = await command.ExecuteScalarAsync();
    if (result is null or DBNull)
    {
        throw new InvalidOperationException("A PostgreSQL validation query returned no value.");
    }

    return (T)Convert.ChangeType(result, typeof(T));
}

static string RequiredAdminValue(string environmentVariable, int maximumLength)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable)?.Trim();
    if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
    {
        throw new InvalidOperationException("A required super-administrator value is missing or too long.");
    }

    return value;
}

static void ClearAdminEnvironment()
{
    Environment.SetEnvironmentVariable("DIAGLINK_SUPER_ADMIN_EMAIL", null);
    Environment.SetEnvironmentVariable("DIAGLINK_SUPER_ADMIN_FIRST_NAME", null);
    Environment.SetEnvironmentVariable("DIAGLINK_SUPER_ADMIN_LAST_NAME", null);
    Environment.SetEnvironmentVariable("DIAGLINK_SUPER_ADMIN_ENTRA_OBJECT_ID", null);
}
