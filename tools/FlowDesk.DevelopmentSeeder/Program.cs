using FlowDesk.Application.Interfaces;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

const string connectionStringVariable =
    "FLOWDESK_DEV_SEED_CONNECTION_STRING";
const string passwordVariable = "FLOWDESK_DEV_SEED_PASSWORD";

if (args.Length != 1 || args[0] != "--seed-development-data")
{
    Console.Error.WriteLine(
        "Run with the explicit --seed-development-data argument.");
    return 2;
}

DevelopmentSeedSafetyPolicy.ValidateEnvironment(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

var rawConnectionString =
    Environment.GetEnvironmentVariable(connectionStringVariable);
if (string.IsNullOrWhiteSpace(rawConnectionString))
{
    throw new InvalidOperationException(
        $"{connectionStringVariable} must be set explicitly.");
}

var connectionSettings =
    DevelopmentSeedSafetyPolicy.ValidateConnectionString(rawConnectionString);

var password = Environment.GetEnvironmentVariable(passwordVariable);
if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
{
    throw new InvalidOperationException(
        $"{passwordVariable} must contain a development password of at least 12 characters.");
}

var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionSettings.ConnectionString));
services.AddScoped<IPasswordHasher, PasswordHasherService>();
services.AddScoped<DevelopmentDataSeeder>();

await using var serviceProvider = services.BuildServiceProvider();
await using var scope = serviceProvider.CreateAsyncScope();
var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await context.Database.OpenConnectionAsync();

await using (var command = context.Database.GetDbConnection().CreateCommand())
{
    command.CommandText =
        "SELECT DB_NAME(), CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));";
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException(
            "Could not verify the connected SQL Server database.");
    }

    var connectedDatabase = reader.GetString(0);
    var connectedServer = reader.GetString(1);
    if (!string.Equals(
            connectedDatabase,
            DevelopmentSeedSafetyPolicy.RequiredDatabase,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "The connected database does not match the required development database.");
    }

    if (!string.Equals(
            connectedServer,
            Environment.MachineName,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "The connected SQL Server host is not the local machine.");
    }
}

var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
var result = await seeder.SeedAsync(password);

Console.WriteLine(result switch
{
    DevelopmentSeedResult.Inserted =>
        "Development test data inserted and verified.",
    DevelopmentSeedResult.AlreadySeeded =>
        "The complete development seed is already present; no duplicates were created.",
    _ => throw new InvalidOperationException("Unknown development seed result.")
});

Console.WriteLine(
    $"The development accounts use the password supplied through {passwordVariable}; its value is not logged.");
foreach (var email in DevelopmentDataSeeder.GetSeedAccountEmails())
{
    Console.WriteLine(email);
}

return 0;
