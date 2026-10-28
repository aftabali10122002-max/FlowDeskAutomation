using Microsoft.Data.SqlClient;

namespace FlowDisk.Api.Tests;

internal static class SqlServerIntegrationTestSafety
{
    public const string ConnectionStringVariable =
        "FLOWDESK_SQLSERVER_TEST_CONNECTION_STRING";
    public const string ApprovedInstanceVariable =
        "FLOWDESK_SQLSERVER_TEST_INSTANCE";
    public const string MigrationApprovalVariable =
        "FLOWDESK_SQLSERVER_TEST_ALLOW_MIGRATIONS";
    public const string RequiredDatabase = "FlowDeskDb_Phase8Integration";
    public const string MigrationApprovalValue = "true";

    public static string ReadValidatedConnectionString(
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.TryGetValue(ConnectionStringVariable, out var connectionString);
        environment.TryGetValue(ApprovedInstanceVariable, out var approvedInstance);
        return ValidateConnectionString(connectionString, approvedInstance);
    }

    public static string ValidateConnectionString(
        string? connectionString,
        string? approvedInstance)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.IsNullOrWhiteSpace(approvedInstance))
        {
            throw new InvalidOperationException(
                "The SQL integration connection and approved instance must both be configured.");
        }

        SqlConnectionStringBuilder settings;
        try
        {
            settings = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "The SQL integration connection configuration is invalid.");
        }

        if (!string.Equals(
                settings.InitialCatalog,
                RequiredDatabase,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                settings.DataSource.Trim(),
                approvedInstance.Trim(),
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(settings.AttachDBFilename) ||
            !string.IsNullOrWhiteSpace(settings.FailoverPartner) ||
            settings.UserInstance)
        {
            throw new InvalidOperationException(
                "The SQL integration connection must target the explicitly approved instance and exact Phase 8 integration database.");
        }

        return connectionString;
    }

    public static void ReadMigrationAuthorization(
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.TryGetValue(MigrationApprovalVariable, out var approval);
        RequireMigrationAuthorization(approval);
    }

    public static void RequireMigrationAuthorization(string? approval)
    {
        if (!string.Equals(
                approval,
                MigrationApprovalValue,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server migration execution requires explicit approval through FLOWDESK_SQLSERVER_TEST_ALLOW_MIGRATIONS=true.");
        }
    }

    public static void RequireMigrationsApplied(
        IEnumerable<string> expectedMigrations,
        IEnumerable<string> appliedMigrations)
    {
        ArgumentNullException.ThrowIfNull(expectedMigrations);
        ArgumentNullException.ThrowIfNull(appliedMigrations);

        var applied = appliedMigrations.ToHashSet(StringComparer.Ordinal);
        var missing = expectedMigrations
            .Where(migration => !applied.Contains(migration))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "The Phase 8 integration database is missing required EF migrations: " +
                string.Join(", ", missing));
        }
    }

    public static void RequireConnectedTarget(
        string? databaseName,
        string? serverName,
        string? approvedInstance)
    {
        if (!string.Equals(
                databaseName,
                RequiredDatabase,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(serverName) ||
            string.IsNullOrWhiteSpace(approvedInstance) ||
            !string.Equals(
                serverName.Trim(),
                approvedInstance.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The connected SQL Server identity does not match the explicitly approved test target.");
        }
    }

    public static Dictionary<string, string?> ReadRelevantEnvironment()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ConnectionStringVariable] =
                Environment.GetEnvironmentVariable(ConnectionStringVariable),
            [ApprovedInstanceVariable] =
                Environment.GetEnvironmentVariable(ApprovedInstanceVariable)
        };
    }
}
