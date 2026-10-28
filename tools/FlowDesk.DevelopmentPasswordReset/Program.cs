using System.Text;
using FlowDesk.Application.Interfaces;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

const string connectionStringVariable =
    "FLOWDESK_DEV_PASSWORD_RESET_CONNECTION_STRING";
const string confirmationText =
    "RESET PASSWORDS IN FlowDeskDb_Phase4Test";

try
{
    DevelopmentPasswordResetPolicy.ValidateEnvironment(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

    var emails = ParseSelectedEmails(args);
    var rawConnectionString =
        Environment.GetEnvironmentVariable(connectionStringVariable);
    if (string.IsNullOrWhiteSpace(rawConnectionString))
    {
        throw new InvalidOperationException(
            $"{connectionStringVariable} must be set explicitly.");
    }

    var connectionSettings =
        DevelopmentPasswordResetPolicy.ValidateConnectionString(
            rawConnectionString);
    var services = new ServiceCollection();
    services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionSettings.ConnectionString));
    services.AddScoped<IPasswordHasher, PasswordHasherService>();
    services.AddScoped<DevelopmentPasswordResetService>();

    await using var serviceProvider = services.BuildServiceProvider();
    await using var scope = serviceProvider.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.OpenConnectionAsync();
    await VerifyConnectedDatabaseAsync(context);

    Console.WriteLine(
        $"Selected accounts in {DevelopmentPasswordResetPolicy.RequiredDatabase}:");
    foreach (var email in emails)
    {
        Console.WriteLine($"  {email}");
    }

    Console.WriteLine(
        $"Type '{confirmationText}' to continue, or anything else to cancel:");
    if (!string.Equals(
            Console.ReadLine(),
            confirmationText,
            StringComparison.Ordinal))
    {
        Console.WriteLine("Password reset cancelled. No changes were made.");
        return 0;
    }

    var password = ReadSecret("New password (12+ characters): ");
    var confirmation = ReadSecret("Re-enter new password: ");
    if (!string.Equals(password, confirmation, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The entered passwords did not match. No changes were made.");
    }

    var resetService = scope.ServiceProvider
        .GetRequiredService<DevelopmentPasswordResetService>();
    var resetCount = await resetService.ResetAsync(emails, password);
    Console.WriteLine(
        $"Updated password hashes for {resetCount} selected account(s) in {DevelopmentPasswordResetPolicy.RequiredDatabase}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string[] ParseSelectedEmails(string[] arguments)
{
    if (arguments.Length < 3 ||
        arguments[0] != "--reset-development-passwords")
    {
        throw new ArgumentException(
            "Use --reset-development-passwords followed by one or more --email <address> selections.");
    }

    var emails = new List<string>();
    for (var index = 1; index < arguments.Length; index++)
    {
        if (arguments[index] != "--email" ||
            index + 1 >= arguments.Length ||
            arguments[index + 1].StartsWith(
                "--",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Each selected account must be provided as --email <address>. No --all option is supported.");
        }

        emails.Add(arguments[++index]);
    }

    if (emails.Count == 0)
    {
        throw new ArgumentException(
            "Select at least one account with --email <address>.");
    }

    if (emails.Any(string.IsNullOrWhiteSpace))
    {
        throw new ArgumentException(
            "Selected account emails cannot be empty.");
    }

    if (emails.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
        emails.Count)
    {
        throw new ArgumentException(
            "Selected account emails must be unique.");
    }

    return emails.ToArray();
}

static async Task VerifyConnectedDatabaseAsync(AppDbContext context)
{
    await using var command = context.Database
        .GetDbConnection()
        .CreateCommand();
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
            DevelopmentPasswordResetPolicy.RequiredDatabase,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Connected database verification failed. No password hashes were changed.");
    }

    if (!string.Equals(
            connectedServer,
            Environment.MachineName,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Connected SQL Server host verification failed. No password hashes were changed.");
    }
}

static string ReadSecret(string prompt)
{
    Console.Write(prompt);
    var characters = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            var secret = characters.ToString();
            characters.Clear();
            return secret;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Length > 0)
            {
                characters.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            characters.Append(key.KeyChar);
        }
    }
}
