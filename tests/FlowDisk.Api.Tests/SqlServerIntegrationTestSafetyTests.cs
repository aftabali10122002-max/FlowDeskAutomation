using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class SqlServerIntegrationTestSafetyTests
{
    private const string ApprovedInstance = @".\FLOWDESK_PHASE8_TEST";

    [Fact]
    public void ValidateConnectionString_AcceptsExactDatabaseAndApprovedInstance()
    {
        var connectionString =
            $@"Server={ApprovedInstance};Database={SqlServerIntegrationTestSafety.RequiredDatabase};Integrated Security=True;TrustServerCertificate=True;";

        var validated = SqlServerIntegrationTestSafety.ValidateConnectionString(
            connectionString,
            ApprovedInstance);

        Assert.Same(connectionString, validated);
    }

    [Theory]
    [InlineData("FlowDeskDb_New")]
    [InlineData("FlowDeskDb_Phase4Test")]
    [InlineData("UnexpectedDatabase")]
    public void ValidateConnectionString_RejectsAnyOtherDatabase(
        string databaseName)
    {
        var connectionString =
            $@"Server={ApprovedInstance};Database={databaseName};Integrated Security=True;";

        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.ValidateConnectionString(
                connectionString,
                ApprovedInstance));
    }

    [Fact]
    public void ValidateConnectionString_RejectsUnexpectedInstance()
    {
        var connectionString =
            $@"Server=localhost;Database={SqlServerIntegrationTestSafety.RequiredDatabase};Integrated Security=True;";

        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.ValidateConnectionString(
                connectionString,
                ApprovedInstance));
    }

    [Theory]
    [InlineData(null, ApprovedInstance)]
    [InlineData("", ApprovedInstance)]
    [InlineData("Server=.;Database=FlowDeskDb_Phase8Integration;", null)]
    [InlineData("not a connection string", ApprovedInstance)]
    public void ValidateConnectionString_RejectsMissingOrInvalidConfiguration(
        string? connectionString,
        string? approvedInstance)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.ValidateConnectionString(
                connectionString,
                approvedInstance));
    }

    [Fact]
    public void ReadValidatedConnectionString_DoesNotUseApplicationOverrides()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] =
                "Server=localhost;Database=FlowDeskDb_New;",
            [SqlServerIntegrationTestSafety.ApprovedInstanceVariable] =
                ApprovedInstance
        };

        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.ReadValidatedConnectionString(
                environment));

        var explicitTestConnection =
            $@"Server={ApprovedInstance};Database={SqlServerIntegrationTestSafety.RequiredDatabase};Integrated Security=True;";
        environment[SqlServerIntegrationTestSafety.ConnectionStringVariable] =
            explicitTestConnection;

        Assert.Same(
            explicitTestConnection,
            SqlServerIntegrationTestSafety.ReadValidatedConnectionString(
                environment));
    }

    [Fact]
    public void RequireMigrationAuthorization_FailsClosedUnlessExplicitlyApproved()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.RequireMigrationAuthorization(null));
        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.RequireMigrationAuthorization("false"));

        SqlServerIntegrationTestSafety.RequireMigrationAuthorization("true");
    }

    [Fact]
    public void ReadMigrationAuthorization_RequiresDedicatedExplicitSetting()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ALLOW_MIGRATIONS"] = "true"
        };

        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.ReadMigrationAuthorization(
                environment));

        environment[SqlServerIntegrationTestSafety.MigrationApprovalVariable] =
            "true";
        SqlServerIntegrationTestSafety.ReadMigrationAuthorization(environment);
    }

    [Fact]
    public void RequireConnectedTargetAcceptsOnlyExactApprovedIdentity()
    {
        SqlServerIntegrationTestSafety.RequireConnectedTarget(
            SqlServerIntegrationTestSafety.RequiredDatabase,
            ApprovedInstance,
            ApprovedInstance);

        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.RequireConnectedTarget(
                "FlowDeskDb_Phase4Test",
                ApprovedInstance,
                ApprovedInstance));
        Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.RequireConnectedTarget(
                SqlServerIntegrationTestSafety.RequiredDatabase,
                "unexpected-server",
                ApprovedInstance));
    }

    [Fact]
    public void RequireMigrationsAppliedFailsWhenExpectedMigrationIsMissing()
    {
        SqlServerIntegrationTestSafety.RequireMigrationsApplied(
            ["20260101000000_Initial", "20260102000000_Leave"],
            ["20260101000000_Initial", "20260102000000_Leave"]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestSafety.RequireMigrationsApplied(
                ["20260101000000_Initial", "20260102000000_Leave"],
                ["20260101000000_Initial"]));

        Assert.Contains("20260102000000_Leave", exception.Message);
    }
}
