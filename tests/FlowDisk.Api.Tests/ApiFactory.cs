using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using FlowDisk.Api.Attachments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FlowDisk.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "FlowDesk.Tests";
    public const string Audience = "FlowDesk.Tests";
    public const string TestSigningKey =
        "FlowDeskTestSigningKeyOnlyNeverUseInProduction2026";

    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");
    private readonly string _profileImageStoragePath = Path.Combine(
        Path.GetTempPath(),
        "FlowDesk.Api.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _requestAttachmentStoragePath = Path.Combine(
        Path.GetTempPath(),
        "FlowDesk.Api.Tests",
        Guid.NewGuid().ToString("N"));

    public string ProfileImageStoragePath => _profileImageStoragePath;
    public string RequestAttachmentStoragePath =>
        _requestAttachmentStoragePath;
    public IAttachmentMalwareScanner? TestAttachmentMalwareScanner { get; set; }
    public int AttachmentUploadConcurrencyLimit { get; set; } = 2;
    public int AttachmentScanMaximumAttempts { get; set; } = 6;
    public int AttachmentScanRetryBaseSeconds { get; set; } = 30;
    public int AttachmentCleanupBatchSize { get; set; } = 25;
    public IPrivateFileStorage? TestPrivateFileStorage { get; set; }
    public int ReportExportMaxRows { get; set; } = 10_000;

    static ApiFactory()
    {
        ConfigureTestEnvironment();
    }

        public static void ConfigureTestEnvironment()
    {
        Environment.SetEnvironmentVariable(
            "Jwt__SecretKey",
            TestSigningKey);
        Environment.SetEnvironmentVariable(
            "Jwt__Issuer",
            Issuer);
        Environment.SetEnvironmentVariable(
            "Jwt__Audience",
            Audience);
        Environment.SetEnvironmentVariable(
            "Jwt__ExpirationMinutes",
            "30");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=unused;Database=unused;");
    }

    public ApiFactory()
    {
        _connection.Open();
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public HttpClient CreateAuthorizedClient(int userId, string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                CreateToken(userId, role));
        return client;
    }

    public static string CreateToken(int userId, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"Test {userId}"),
            new Claim(ClaimTypes.Email, $"test{userId}@example.test"),
            new Claim(ClaimTypes.Role, role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(TestSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<User> AddUserAsync(string role = "Employee")
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unique = Guid.NewGuid().ToString("N");
        var user = new User(
            $"Test {unique}",
            $"{unique}@example.test",
            "not-a-real-password-hash");

        context.Users.Add(user);
        await context.SaveChangesAsync();

        if (role != "Employee")
        {
            context.Entry(user).Property(x => x.Role).CurrentValue = role;
            await context.SaveChangesAsync();
        }

        return user;
    }

    public async Task AddDefaultCompanyLeavePolicyAsync(int adminUserId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy = new LeavePolicy(
            "TEST-COMPANY",
            "Test company leave policy",
            1,
            LeavePolicyScope.Company,
            null,
            null,
            null,
            new DateOnly(2020, 1, 1),
            null,
            LeaveDayCalculationMethod.CalendarDays,
            null,
            null,
            allowsHalfDays: false,
            halfDayUnits: null,
            isAccrualEnabled: false,
            accrualFrequency: null,
            allowsCarryForward: false,
            carryForwardCap: null,
            carryForwardExpiryMonths: null,
            probationPeriodDays: null,
            allowsLeaveDuringProbation: true,
            allowsNegativeBalance: true);
        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();
        foreach (var leaveTypeId in new[] { 1, 2, 3, 4 })
        {
            policy.AddEntitlement(new LeavePolicyEntitlement(
                policy.Id,
                leaveTypeId,
                20m));
        }

        policy.Publish(adminUserId, DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    public async Task<ApprovalRequest> AddRequestAsync(
        int? ownerUserId,
        int? departmentId = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = ownerUserId is int ownerId
            ? new ApprovalRequest("Test request", 25m, ownerId, departmentId)
            : new ApprovalRequest("Legacy request", 25m);

        context.ApprovalRequests.Add(request);
        await context.SaveChangesAsync();
        return request;
    }

    public async Task AddAssignmentAsync(
        int requestId,
        int approverUserId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.ApprovalAssignments.Add(
            new ApprovalAssignment(requestId, approverUserId));
        await context.SaveChangesAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=unused;Database=unused;",
                ["Jwt:SecretKey"] = TestSigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:ExpirationMinutes"] = "30",
                ["RateLimiting:Login:PermitLimit"] = "10",
                ["RateLimiting:Registration:PermitLimit"] = "5",
                ["Reports:ExportMaxRows"] = ReportExportMaxRows.ToString(),
                ["Leave:BusinessTimeZoneId"] = "UTC",
                ["ProfileImages:StoragePath"] = _profileImageStoragePath,
                ["Attachments:StoragePath"] = _requestAttachmentStoragePath,
                ["Attachments:Scanner:MaximumAttempts"] =
                    AttachmentScanMaximumAttempts.ToString(),
                ["Attachments:Scanner:RetryBaseSeconds"] =
                    AttachmentScanRetryBaseSeconds.ToString()
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RequestAttachmentOptions>();
            services.AddSingleton(RequestAttachmentOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Attachments:MaximumConcurrentUploadsPerUser"] =
                            AttachmentUploadConcurrencyLimit.ToString(),
                        ["Attachments:Scanner:MaximumAttempts"] =
                            AttachmentScanMaximumAttempts.ToString(),
                        ["Attachments:Scanner:RetryBaseSeconds"] =
                            AttachmentScanRetryBaseSeconds.ToString(),
                        ["Attachments:CleanupBatchSize"] =
                            AttachmentCleanupBatchSize.ToString(),
                        ["Attachments:OrphanFileGraceHours"] = "24"
                    })
                    .Build()));
            if (TestPrivateFileStorage is not null)
            {
                services.RemoveAll<IPrivateFileStorage>();
                services.AddSingleton(TestPrivateFileStorage);
            }
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
            if (TestAttachmentMalwareScanner is not null)
            {
                services.RemoveAll<IAttachmentMalwareScanner>();
                services.AddSingleton(TestAttachmentMalwareScanner);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_profileImageStoragePath))
            {
                Directory.Delete(_profileImageStoragePath, recursive: true);
            }
            if (Directory.Exists(_requestAttachmentStoragePath))
            {
                Directory.Delete(_requestAttachmentStoragePath, recursive: true);
            }
        }
    }
}
