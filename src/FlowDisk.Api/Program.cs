using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.Services;
using FlowDesk.Infrastructure.Persistence;
using FlowDesk.Infrastructure.Repositories;
using FlowDesk.Infrastructure.Security;
using FlowDisk.Api.Exceptions;
using FlowDisk.Api.Hubs;
using FlowDisk.Api.Leave;
using FlowDisk.Api.Notifications;
using FlowDisk.Api.Reports;
using FlowDisk.Api.Security;
using FlowDisk.Api.Attachments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();

var loginPermitLimit = GetPositiveRateLimit(
    builder.Configuration,
    "RateLimiting:Login:PermitLimit");
var registrationPermitLimit = GetPositiveRateLimit(
    builder.Configuration,
    "RateLimiting:Registration:PermitLimit");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType =
            "application/problem+json";
        var retryAfter = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var retryAfterValue)
            ? retryAfterValue
            : TimeSpan.FromMinutes(1);
        context.HttpContext.Response.Headers["Retry-After"] =
            Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Please wait before trying again."
            },
            cancellationToken);
    };

    options.AddPolicy(
        RequestPolicies.LoginRateLimit,
        context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(
        RequestPolicies.RegistrationRateLimit,
        context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = registrationPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT access token ko Authorize mein enter karein.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "DefaultConnection is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<
    IApprovalRequestRepository,
    ApprovalRequestRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<
    IRequestAttachmentRepository,
    RequestAttachmentRepository>();
builder.Services.AddScoped<AttachmentIntakeService>();
builder.Services.AddSingleton<AttachmentUploadConcurrencyLimiter>();
builder.Services.AddScoped<AttachmentUploadConcurrencyResourceFilter>();
builder.Services.AddScoped<
    IApprovalWorkflowRepository,
    ApprovalWorkflowRepository>();
builder.Services.AddScoped<
    IUserRepository,
    UserRepository>();
builder.Services.AddScoped<
    IRequestTypeRepository,
    RequestTypeRepository>();
builder.Services.AddScoped<
    IDepartmentRepository,
    DepartmentRepository>();
builder.Services.AddScoped<
    IDashboardAnalyticsRepository,
    DashboardAnalyticsRepository>();
builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
builder.Services.AddScoped<
    ILeaveManagementRepository,
    LeaveManagementRepository>();
builder.Services.AddScoped<
    ILeaveAccountingRepository,
    LeaveAccountingRepository>();
builder.Services.AddScoped<
    ILeaveCatalogRepository,
    LeaveCatalogRepository>();

builder.Services.AddScoped<
    IPasswordHasher,
    PasswordHasherService>();
builder.Services.AddScoped<
    IJwtTokenService,
    JwtTokenService>();

builder.Services.AddScoped<ApprovalRequestService>();
builder.Services.AddScoped<ApprovalWorkflowService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AdminManagementService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<RequestTypeService>();
builder.Services.AddScoped<DashboardAnalyticsService>();
builder.Services.AddScoped<ReportsService>();
builder.Services.AddScoped<ReportsExportService>();
builder.Services.AddScoped<ILeavePolicyService, LeavePolicyService>();
builder.Services.AddScoped<
    IEmployeeEmploymentProfileService,
    EmployeeEmploymentProfileService>();
builder.Services.AddScoped<ILeaveRequestPreparationService, LeaveRequestPreparationService>();
builder.Services.AddScoped<LeaveAccountingService>();
builder.Services.AddScoped<LeaveCatalogService>();
builder.Services.AddSingleton<IPrivateFileStorage>(_ =>
    new LocalPrivateFileStorage(
        builder.Environment.ContentRootPath,
        builder.Environment.WebRootPath,
        builder.Configuration));
builder.Services.AddSingleton<IProfileImageStorage, LocalProfileImageStorage>();
builder.Services.AddSingleton(
    RequestAttachmentOptions.FromConfiguration(builder.Configuration));
var scannerProvider = builder.Configuration[
    "Attachments:Scanner:Provider"];
switch (scannerProvider?.Trim().ToLowerInvariant())
{
    case null or "" or "unconfigured":
        builder.Services.AddSingleton<
            IAttachmentMalwareScanner,
            UnconfiguredAttachmentMalwareScanner>();
        break;
    case "clamav":
        builder.Services.AddSingleton(
            ClamAvAttachmentScannerOptions.FromConfiguration(
                builder.Configuration));
        builder.Services.AddSingleton<
            IAttachmentMalwareScanner,
            ClamAvAttachmentMalwareScanner>();
        break;
    default:
        throw new InvalidOperationException(
            "Attachments:Scanner:Provider must be 'Unconfigured' or 'ClamAV'.");
}
builder.Services.AddSingleton<
    IWorkflowNotificationPublisher,
    SignalRWorkflowNotificationPublisher>();
builder.Services.AddSingleton<
    IWorkflowEmailSender,
    SmtpWorkflowEmailSender>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<WorkflowNotificationWorker>();
    builder.Services.AddHostedService<LeaveBalanceMaintenanceWorker>();
    builder.Services.AddHostedService<RequestAttachmentScanWorker>();
    builder.Services.AddHostedService<RequestAttachmentStorageCleanupWorker>();
}


var secretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException(
        "JWT SecretKey is missing.");
var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT Issuer is missing.");
var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT Audience is missing.");

if (Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    throw new InvalidOperationException(
        "JWT SecretKey must contain at least 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(secretKey)),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments(
                        "/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        RequestPolicies.ReadAllRequests,
        policy => policy.RequireAuthenticatedUser()
            .RequireRole("Admin"));

    options.AddPolicy(
        RequestPolicies.ReviewLegacyRequests,
        policy => policy.RequireAuthenticatedUser()
            .RequireRole("Admin"));

    options.AddPolicy(
        RequestPolicies.ManageApprovalAssignments,
        policy => policy.RequireAuthenticatedUser()
            .RequireRole("Admin"));
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapControllers();
app.MapHub<WorkflowNotificationsHub>("/hubs/notifications")
    .RequireAuthorization();
app.Run();

static int GetPositiveRateLimit(
    IConfiguration configuration,
    string key)
{
    var value = configuration.GetValue<int?>(key);
    if (value is null or <= 0)
    {
        throw new InvalidOperationException(
            $"Rate limit configuration '{key}' must be a positive integer.");
    }

    return value.Value;
}

static string GetClientPartitionKey(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

public partial class Program;
