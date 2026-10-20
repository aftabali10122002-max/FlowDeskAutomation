using Microsoft.Data.SqlClient;

namespace FlowDesk.Infrastructure.Persistence;

public static class DevelopmentSeedSafetyPolicy
{
    public const string RequiredEnvironment = "Development";
    public const string RequiredDatabase = "FlowDeskDb_New";

    public static void ValidateEnvironment(
        string? aspNetCoreEnvironment,
        string? dotNetEnvironment)
    {
        var values = new[] { aspNetCoreEnvironment, dotNetEnvironment }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (values.Length == 0 ||
            values.Any(value => !string.Equals(
                value,
                RequiredEnvironment,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The development seeder requires ASPNETCORE_ENVIRONMENT and/or DOTNET_ENVIRONMENT to be Development, and rejects other configured environments.");
        }
    }

    public static SqlConnectionStringBuilder ValidateConnectionString(
        string connectionString)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        var server = settings.DataSource.Trim();
        var host = server.Split(['\\', ','], 2)[0].Trim('[', ']');
        var isLocalHost = string.Equals(
            host,
            "localhost",
            StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

        if (!isLocalHost ||
            !string.Equals(
                settings.InitialCatalog,
                RequiredDatabase,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(settings.AttachDBFilename) ||
            !string.IsNullOrWhiteSpace(settings.FailoverPartner))
        {
            throw new InvalidOperationException(
                $"The seeder only permits SQL Server on localhost targeting '{RequiredDatabase}'.");
        }

        return settings;
    }
}
