using System.Data;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Persistence;

public enum DevelopmentSeedResult
{
    Inserted,
    AlreadySeeded
}

public sealed class DevelopmentDataSeeder(
    AppDbContext context,
    IPasswordHasher passwordHasher)
{
    private const string SeedEmailPrefix = "flowdesk.seed.v1.";

    private static readonly DepartmentSeedPlan[] DepartmentPlans =
    [
        new("IT", 5, 2, 30, 9, 12, 6, 3),
        new("HR", 4, 1, 20, 6, 8, 4, 2),
        new("Finance", 5, 2, 30, 9, 12, 6, 3),
        new("Marketing", 4, 1, 20, 6, 8, 4, 2)
    ];

    private static readonly string[] RequestTitles =
    [
        "Laptop replacement for project team",
        "Annual software license renewal",
        "Workstation memory and storage upgrade",
        "Department training and certification",
        "Secure network equipment replacement",
        "Cloud service capacity renewal",
        "Ergonomic workstation equipment",
        "Quarterly team travel and lodging"
    ];

    private static readonly decimal[] RequestAmounts =
    [
        1249.99m,
        399.00m,
        875.50m,
        1450.00m,
        2195.75m,
        680.00m,
        529.95m,
        1875.00m
    ];

    public async Task<DevelopmentSeedResult> SeedAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException(
                "The development seed password must be at least 12 characters.",
                nameof(password));
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var departments = await LoadDepartmentsAsync(cancellationToken);
            var userPlans = BuildUserPlans(departments);
            var seededEmails = userPlans
                .Select(x => x.Email)
                .ToArray();
            var existingSeedUsers = await context.Users
                .Where(x => seededEmails.Contains(x.Email))
                .ToListAsync(cancellationToken);

            if (existingSeedUsers.Count > 0)
            {
                await ValidateExistingSeedAsync(
                    existingSeedUsers,
                    userPlans,
                    departments,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return DevelopmentSeedResult.AlreadySeeded;
            }

            var requestPlans = BuildRequestPlans(userPlans, departments);
            var requestTitles = requestPlans
                .Select(x => x.Title)
                .ToArray();
            var existingSeedRequest = await context.ApprovalRequests
                .AnyAsync(
                    x => requestTitles.Contains(x.Title),
                    cancellationToken);
            if (existingSeedRequest)
            {
                throw new InvalidOperationException(
                    "A deterministic development seed request title already exists without its seed accounts; refusing to create duplicates.");
            }

            var seedStartedAtUtc = DateTime.UtcNow;
            var users = userPlans
                .Select((plan, index) =>
                {
                    var user = new User(
                        plan.FullName,
                        plan.Email,
                        passwordHasher.Hash(password));
                    user.ChangeRole(plan.Role);
                    context.Entry(user)
                        .Property(x => x.DepartmentId)
                        .CurrentValue = plan.DepartmentId;
                    context.Entry(user)
                        .Property(x => x.CreatedAtUtc)
                        .CurrentValue = seedStartedAtUtc.AddDays(-120 + index);
                    return user;
                })
                .ToArray();

            context.Users.AddRange(users);
            await context.SaveChangesAsync(cancellationToken);

            var userByEmail = users.ToDictionary(x => x.Email);
            var now = DateTime.UtcNow;
            var requests = requestPlans
                .Select((plan, index) =>
                {
                    var createdAtUtc = now
                        .AddDays(-75 + (index % 60))
                        .AddMinutes(index % 1440);
                    var request = new ApprovalRequest(
                        plan.Title,
                        plan.Amount,
                        userByEmail[plan.EmployeeEmail].Id,
                        plan.DepartmentId);
                    SetStatus(request, plan.Status);
                    context.ApprovalRequests.Add(request);
                    context.Entry(request)
                        .Property(x => x.CreatedAtUtc)
                        .CurrentValue = createdAtUtc;
                    return (Request: request, Plan: plan, CreatedAtUtc: createdAtUtc);
                })
                .ToArray();

            await context.SaveChangesAsync(cancellationToken);

            var assignments = requests
                .Select((entry, index) =>
                {
                    var assignedAtUtc = entry.CreatedAtUtc
                        .AddMinutes(15 + (index % 166));
                    var assignment = new ApprovalAssignment(
                        entry.Request.Id,
                        userByEmail[entry.Plan.ManagerEmail].Id);
                    context.Entry(assignment)
                        .Property(x => x.AssignedAtUtc)
                        .CurrentValue = assignedAtUtc;

                    if (entry.Plan.Status != RequestStatus.Pending)
                    {
                        var decidedAtUtc = assignedAtUtc
                            .AddHours(1 + (index % 48));
                        SetAssignmentStatus(assignment, entry.Plan.Status, decidedAtUtc);
                    }

                    return assignment;
                })
                .ToArray();

            context.ApprovalAssignments.AddRange(assignments);
            await context.SaveChangesAsync(cancellationToken);
            await ValidatePersistedSeedAsync(
                userPlans,
                departments,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return DevelopmentSeedResult.Inserted;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static IReadOnlyList<string> GetSeedAccountEmails()
    {
        return DepartmentPlans
            .SelectMany(plan => Enumerable.Range(1, plan.EmployeeCount)
                .Select(index => GetEmail(plan.Name, "employee", index))
                .Concat(Enumerable.Range(1, plan.ManagerCount)
                    .Select(index => GetEmail(plan.Name, "manager", index))))
            .ToArray();
    }

    private async Task<Dictionary<string, Department>> LoadDepartmentsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await context.Departments
            .Where(x => DepartmentPlans
                .Select(plan => plan.Name)
                .Contains(x.Name))
            .ToListAsync(cancellationToken);
        var departments = new Dictionary<string, Department>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var plan in DepartmentPlans)
        {
            var matching = existing
                .Where(x => string.Equals(
                    x.Name.Trim(),
                    plan.Name,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matching.Length != 1 || !matching[0].IsActive)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one active existing '{plan.Name}' department.");
            }

            departments.Add(plan.Name, matching[0]);
        }

        return departments;
    }

    private static UserSeedPlan[] BuildUserPlans(
        IReadOnlyDictionary<string, Department> departments)
    {
        return DepartmentPlans
            .SelectMany(plan =>
                Enumerable.Range(1, plan.EmployeeCount)
                    .Select(index => new UserSeedPlan(
                        $"FlowDesk Development {plan.Name} Employee {index:00}",
                        GetEmail(plan.Name, "employee", index),
                        UserRoles.Employee,
                        departments[plan.Name].Id,
                        plan.Name))
                    .Concat(Enumerable.Range(1, plan.ManagerCount)
                        .Select(index => new UserSeedPlan(
                            $"FlowDesk Development {plan.Name} Manager {index:00}",
                            GetEmail(plan.Name, "manager", index),
                            UserRoles.Manager,
                            departments[plan.Name].Id,
                            plan.Name)))
            )
            .ToArray();
    }

    private static RequestSeedPlan[] BuildRequestPlans(
        IReadOnlyCollection<UserSeedPlan> userPlans,
        IReadOnlyDictionary<string, Department> departments)
    {
        return DepartmentPlans
            .SelectMany(departmentPlan =>
            {
                var employees = userPlans
                    .Where(x =>
                        x.DepartmentName == departmentPlan.Name &&
                        x.Role == UserRoles.Employee)
                    .ToArray();
                var managers = userPlans
                    .Where(x =>
                        x.DepartmentName == departmentPlan.Name &&
                        x.Role == UserRoles.Manager)
                    .ToArray();
                var statuses = BuildStatuses(departmentPlan);

                return Enumerable.Range(0, departmentPlan.RequestCount)
                    .Select(index =>
                    {
                        var sequence = index + 1;
                        return new RequestSeedPlan(
                            departmentPlan.Name,
                            departments[departmentPlan.Name].Id,
                            employees[index % employees.Length].Email,
                            managers[index % managers.Length].Email,
                            $"{RequestTitles[index % RequestTitles.Length]} " +
                                $"({departmentPlan.Name}-{sequence:000})",
                            RequestAmounts[index % RequestAmounts.Length],
                            statuses[index]);
                    });
            })
            .ToArray();
    }

    private static RequestStatus[] BuildStatuses(DepartmentSeedPlan plan)
    {
        return Enumerable.Repeat(RequestStatus.Pending, plan.PendingCount)
            .Concat(Enumerable.Repeat(RequestStatus.Approved, plan.ApprovedCount))
            .Concat(Enumerable.Repeat(RequestStatus.Rejected, plan.RejectedCount))
            .Concat(Enumerable.Repeat(RequestStatus.Cancelled, plan.CancelledCount))
            .ToArray();
    }

    private async Task ValidateExistingSeedAsync(
        IReadOnlyCollection<User> existingUsers,
        IReadOnlyCollection<UserSeedPlan> userPlans,
        IReadOnlyDictionary<string, Department> departments,
        CancellationToken cancellationToken)
    {
        if (existingUsers.Count != userPlans.Count)
        {
            throw new InvalidOperationException(
                "Partial development seed accounts already exist; refusing to modify them.");
        }

        var byEmail = existingUsers.ToDictionary(x => x.Email);
        foreach (var plan in userPlans)
        {
            if (!byEmail.TryGetValue(plan.Email, out var user) ||
                user.Role != plan.Role ||
                user.DepartmentId != plan.DepartmentId ||
                user.CreatedAtUtc.Kind == DateTimeKind.Local)
            {
                throw new InvalidOperationException(
                    "Existing development seed accounts do not match the expected seed; refusing changes.");
            }
        }

        await ValidateRequestsAsync(
            existingUsers.Select(x => x.Id).ToArray(),
            userPlans,
            departments,
            cancellationToken);
    }

    private async Task ValidatePersistedSeedAsync(
        IReadOnlyCollection<UserSeedPlan> userPlans,
        IReadOnlyDictionary<string, Department> departments,
        CancellationToken cancellationToken)
    {
        var emails = userPlans.Select(x => x.Email).ToArray();
        var users = await context.Users
            .AsNoTracking()
            .Where(x => emails.Contains(x.Email))
            .ToListAsync(cancellationToken);

        if (users.Count != userPlans.Count)
        {
            throw new InvalidOperationException(
                "The saved development seed has an unexpected user count.");
        }

        foreach (var plan in userPlans)
        {
            var user = users.Single(x => x.Email == plan.Email);
            if (user.Role != plan.Role ||
                user.DepartmentId != plan.DepartmentId ||
                user.CreatedAtUtc.Kind == DateTimeKind.Local)
            {
                throw new InvalidOperationException(
                    "A seeded user has an invalid role, department, or timestamp.");
            }
        }

        await ValidateRequestsAsync(
            users.Select(x => x.Id).ToArray(),
            userPlans,
            departments,
            cancellationToken);
    }

    private async Task ValidateRequestsAsync(
        int[] seededUserIds,
        IReadOnlyCollection<UserSeedPlan> userPlans,
        IReadOnlyDictionary<string, Department> departments,
        CancellationToken cancellationToken)
    {
        var requests = await context.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Department)
            .Include(x => x.Assignments)
                .ThenInclude(x => x.ApproverUser)
            .Where(x => x.CreatedByUserId.HasValue &&
                seededUserIds.Contains(x.CreatedByUserId.Value))
            .ToListAsync(cancellationToken);
        var expectedPlans = BuildRequestPlans(userPlans, departments);

        if (requests.Count != 100 || expectedPlans.Length != 100)
        {
            throw new InvalidOperationException(
                "The development seed must contain exactly 100 owned requests.");
        }

        var expectedByTitle = expectedPlans.ToDictionary(
            x => x.Title,
            StringComparer.Ordinal);
        var statusTotals = Enum.GetValues<RequestStatus>()
            .ToDictionary(status => status, _ => 0);
        var departmentTotals = DepartmentPlans
            .ToDictionary(plan => plan.Name, _ => 0,
                StringComparer.OrdinalIgnoreCase);
        var departmentStatusTotals = DepartmentPlans
            .ToDictionary(
                plan => plan.Name,
                _ => Enum.GetValues<RequestStatus>()
                    .ToDictionary(status => status, _ => 0),
                StringComparer.OrdinalIgnoreCase);
        var nowUtc = DateTime.UtcNow;

        foreach (var request in requests)
        {
            if (!expectedByTitle.TryGetValue(request.Title, out var expectedPlan) ||
                request.Amount != expectedPlan.Amount ||
                request.Status != expectedPlan.Status ||
                request.CreatedByUser?.Email != expectedPlan.EmployeeEmail ||
                request.DepartmentId != expectedPlan.DepartmentId)
            {
                throw new InvalidOperationException(
                    "A seeded request does not match the deterministic seed plan.");
            }

            if (request.CreatedByUser is null ||
                request.Department is null ||
                request.CreatedByUser.Role != UserRoles.Employee ||
                request.CreatedByUser.DepartmentId != request.DepartmentId ||
                request.CreatedByUser.DepartmentId != request.Department.Id ||
                request.CreatedAtUtc.Kind == DateTimeKind.Local ||
                request.CreatedAtUtc > nowUtc ||
                request.CreatedAtUtc < request.CreatedByUser.CreatedAtUtc ||
                request.Amount <= 0 ||
                request.Assignments.Count != 1)
            {
                throw new InvalidOperationException(
                    "A seeded request has an invalid owner, department, amount, timestamp, or assignment count.");
            }

            var assignment = request.Assignments.Single();
            if (assignment.ApproverUser.Role != UserRoles.Manager ||
                assignment.ApproverUser.Email != expectedPlan.ManagerEmail ||
                assignment.ApproverUser.DepartmentId != request.DepartmentId ||
                assignment.ApproverUser.CreatedAtUtc > request.CreatedAtUtc ||
                assignment.AssignedAtUtc.Kind == DateTimeKind.Local ||
                assignment.AssignedAtUtc < request.CreatedAtUtc ||
                assignment.AssignedAtUtc > nowUtc)
            {
                throw new InvalidOperationException(
                    "A seeded request must have a same-department Manager assignment with valid UTC timestamps.");
            }

            var expectedAssignmentStatus = request.Status switch
            {
                RequestStatus.Pending => ApprovalAssignmentStatus.Pending,
                RequestStatus.Approved => ApprovalAssignmentStatus.Approved,
                RequestStatus.Rejected => ApprovalAssignmentStatus.Rejected,
                RequestStatus.Cancelled => ApprovalAssignmentStatus.Cancelled,
                _ => throw new InvalidOperationException(
                    "A seeded request has an unsupported status.")
            };

            if (assignment.Status != expectedAssignmentStatus ||
                (request.Status == RequestStatus.Pending &&
                    assignment.DecidedAtUtc != null) ||
                (request.Status != RequestStatus.Pending &&
                    (assignment.DecidedAtUtc is null ||
                     assignment.DecidedAtUtc.Value.Kind == DateTimeKind.Local ||
                     assignment.DecidedAtUtc.Value < assignment.AssignedAtUtc ||
                     assignment.DecidedAtUtc.Value > nowUtc)))
            {
                throw new InvalidOperationException(
                    "A seeded assignment status or decision timestamp is inconsistent with its request.");
            }

            if (!departmentTotals.ContainsKey(request.Department.Name))
            {
                throw new InvalidOperationException(
                    "A seeded request references an unexpected department.");
            }

            statusTotals[request.Status]++;
            departmentTotals[request.Department.Name]++;
            departmentStatusTotals[request.Department.Name][request.Status]++;
        }

        var hasExpectedTotals =
            statusTotals[RequestStatus.Pending] == 30 &&
            statusTotals[RequestStatus.Approved] == 40 &&
            statusTotals[RequestStatus.Rejected] == 20 &&
            statusTotals[RequestStatus.Cancelled] == 10 &&
            departmentTotals["IT"] == 30 &&
            departmentTotals["HR"] == 20 &&
            departmentTotals["Finance"] == 30 &&
            departmentTotals["Marketing"] == 20;
        var hasExpectedDepartmentStatusTotals = DepartmentPlans.All(plan =>
        {
            var totals = departmentStatusTotals[plan.Name];
            return totals[RequestStatus.Pending] == plan.PendingCount &&
                totals[RequestStatus.Approved] == plan.ApprovedCount &&
                totals[RequestStatus.Rejected] == plan.RejectedCount &&
                totals[RequestStatus.Cancelled] == plan.CancelledCount;
        });

        if (!hasExpectedTotals || !hasExpectedDepartmentStatusTotals)
        {
            throw new InvalidOperationException(
                "The development seed status or department distribution is invalid.");
        }
    }

    private static void SetStatus(
        ApprovalRequest request,
        RequestStatus status)
    {
        switch (status)
        {
            case RequestStatus.Pending:
                return;
            case RequestStatus.Approved:
                request.Approve();
                return;
            case RequestStatus.Rejected:
                request.Reject();
                return;
            case RequestStatus.Cancelled:
                request.Cancel();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static void SetAssignmentStatus(
        ApprovalAssignment assignment,
        RequestStatus status,
        DateTime decidedAtUtc)
    {
        switch (status)
        {
            case RequestStatus.Approved:
                assignment.MarkApproved(decidedAtUtc);
                return;
            case RequestStatus.Rejected:
                assignment.MarkRejected(decidedAtUtc);
                return;
            case RequestStatus.Cancelled:
                assignment.MarkCancelled(decidedAtUtc);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private static string GetEmail(
        string department,
        string role,
        int index)
    {
        return $"{SeedEmailPrefix}{role}.{department.ToLowerInvariant()}.{index:00}" +
            "@dev.flowdesk.test";
    }

    private sealed record DepartmentSeedPlan(
        string Name,
        int EmployeeCount,
        int ManagerCount,
        int RequestCount,
        int PendingCount,
        int ApprovedCount,
        int RejectedCount,
        int CancelledCount);

    private sealed record UserSeedPlan(
        string FullName,
        string Email,
        string Role,
        int DepartmentId,
        string DepartmentName);

    private sealed record RequestSeedPlan(
        string DepartmentName,
        int DepartmentId,
        string EmployeeEmail,
        string ManagerEmail,
        string Title,
        decimal Amount,
        RequestStatus Status);
}
