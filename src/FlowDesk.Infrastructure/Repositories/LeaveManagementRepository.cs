using System.Data;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class LeaveManagementRepository(
    AppDbContext context) : ILeaveManagementRepository
{
    public Task<LeavePolicy?> GetPolicyAsync(
        int policyId,
        bool tracked = false)
    {
        var query = context.LeavePolicies
            .Include(x => x.Entitlements)
                .ThenInclude(x => x.LeaveType)
            .Where(x => x.Id == policyId);
        return tracked
            ? query.SingleOrDefaultAsync()
            : query.AsNoTracking().SingleOrDefaultAsync();
    }

    public async Task<IReadOnlyList<LeavePolicy>> GetPoliciesAsync()
    {
        return await context.LeavePolicies
            .AsNoTracking()
            .Include(x => x.Entitlements)
                .ThenInclude(x => x.LeaveType)
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Version)
            .ToListAsync();
    }

    public async Task CreateDraftAsync(
        LeavePolicy policy,
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var exists = await context.LeavePolicies.AnyAsync(existing =>
            existing.Code == policy.Code &&
            existing.Version == policy.Version &&
            existing.Scope == policy.Scope &&
            existing.DepartmentId == policy.DepartmentId &&
            existing.DesignationId == policy.DesignationId &&
            existing.EmploymentType == policy.EmploymentType);
        if (exists)
        {
            throw new LeaveOverlapException(
                "That leave policy code and version already exist.");
        }

        context.LeavePolicies.Add(policy);
        await context.SaveChangesAsync();
        AddEntitlements(policy, entitlements);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task UpdateDraftAsync(
        int policyId,
        LeavePolicy revisedPolicy,
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var policy = await context.LeavePolicies
            .Include(x => x.Entitlements)
            .SingleOrDefaultAsync(x => x.Id == policyId);
        if (policy is null)
        {
            throw new LeaveNotFoundException("Leave policy");
        }

        if (policy.Status != LeavePolicyStatus.Draft)
        {
            throw new LeaveConfigurationException(
                "Published policy versions are immutable.");
        }

        policy.ReplaceDraftConfiguration(revisedPolicy);
        context.LeavePolicyEntitlements.RemoveRange(policy.Entitlements);
        policy.ClearDraftEntitlements();
        await context.SaveChangesAsync();
        AddEntitlements(policy, entitlements);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<LeavePolicy> CreateVersionDraftAsync(
        int sourcePolicyId,
        DateOnly effectiveFrom)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var source = await context.LeavePolicies
            .AsNoTracking()
            .Include(x => x.Entitlements)
            .SingleOrDefaultAsync(x => x.Id == sourcePolicyId);
        if (source is null)
        {
            throw new LeaveNotFoundException("Leave policy");
        }

        if (source.Status == LeavePolicyStatus.Draft)
        {
            throw new LeaveConfigurationException(
                "A new version can only be created from a published policy.");
        }

        if (effectiveFrom <= source.EffectiveFrom)
        {
            throw new LeaveInvalidInputException(
                "A new policy version must have a later effective date.");
        }

        var latestVersion = await context.LeavePolicies
            .Where(x =>
                x.Code == source.Code &&
                x.Scope == source.Scope &&
                x.DepartmentId == source.DepartmentId &&
                x.DesignationId == source.DesignationId &&
                x.EmploymentType == source.EmploymentType)
            .MaxAsync(x => (int?)x.Version) ?? 0;
        if (latestVersion == int.MaxValue)
        {
            throw new LeaveConfigurationException(
                "The policy version limit has been reached.");
        }

        var draft = ClonePolicy(source, latestVersion + 1, effectiveFrom);
        context.LeavePolicies.Add(draft);
        await context.SaveChangesAsync();
        AddEntitlements(
            draft,
            source.Entitlements
                .Select(x => new LeavePolicyEntitlementInput(
                    x.LeaveTypeId,
                    x.AnnualEntitlement))
                .ToList());
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return draft;
    }

    public async Task PublishPolicyAsync(
        int policyId,
        int adminUserId,
        DateTime publishedAtUtc)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var policy = await context.LeavePolicies
            .Include(x => x.Entitlements)
                .ThenInclude(x => x.LeaveType)
            .SingleOrDefaultAsync(x => x.Id == policyId);
        if (policy is null)
        {
            throw new LeaveNotFoundException("Leave policy");
        }

        if (policy.Status != LeavePolicyStatus.Draft)
        {
            throw new LeaveConfigurationException(
                "Only a draft leave policy can be published.");
        }

        await ValidatePolicyReferencesAsync(policy);
        await ValidatePolicyEntitlementsAsync(policy);
        await ValidatePolicyPublicationRangeAsync(policy);
        policy.Publish(adminUserId, publishedAtUtc);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task MarkSupersededAsync(
        int policyId,
        int successorPolicyId,
        DateTime supersededAtUtc)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var policies = await context.LeavePolicies
            .Where(x => x.Id == policyId || x.Id == successorPolicyId)
            .ToListAsync();
        var current = policies.SingleOrDefault(x => x.Id == policyId)
            ?? throw new LeaveNotFoundException("Leave policy");
        var successor = policies.SingleOrDefault(
            x => x.Id == successorPolicyId)
            ?? throw new LeaveNotFoundException("Successor leave policy");
        current.MarkSupersededBy(successor, supersededAtUtc);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task CreateAssignmentAsync(LeavePolicyAssignment assignment)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var employee = await context.Users.SingleOrDefaultAsync(
            x => x.Id == assignment.UserId &&
                x.IsActive &&
                x.Role == UserRoles.Employee);
        if (employee is null)
        {
            throw new LeaveNotFoundException("Active employee");
        }

        var policy = await context.LeavePolicies
            .SingleOrDefaultAsync(x => x.Id == assignment.LeavePolicyId);
        if (policy is null ||
            policy.Status is not (LeavePolicyStatus.Published or
                LeavePolicyStatus.Superseded))
        {
            throw new LeaveConfigurationException(
                "An employee can only be assigned a published leave policy.");
        }

        var overlapping = await context.LeavePolicyAssignments.AnyAsync(
            existing =>
                existing.UserId == assignment.UserId &&
                (!assignment.EffectiveTo.HasValue ||
                 existing.EffectiveFrom <= assignment.EffectiveTo.Value) &&
                (!existing.EffectiveTo.HasValue ||
                 existing.EffectiveTo >= assignment.EffectiveFrom));
        if (overlapping)
        {
            throw new LeaveOverlapException(
                "The employee already has a policy assignment overlapping those inclusive dates.");
        }

        context.LeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task CreateEmploymentProfileAsync(
        EmployeeEmploymentProfile profile)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await context.Users.AnyAsync(
                x => x.Id == profile.UserId &&
                x.IsActive &&
                x.Role == UserRoles.Employee))
        {
            throw new LeaveNotFoundException("Active employee");
        }

        if (profile.DesignationId is int designationId &&
            !await context.Designations.AnyAsync(
                x => x.Id == designationId && x.IsEnabled))
        {
            throw new LeaveInvalidInputException(
                "The designation does not exist or is disabled.");
        }

        var overlapping = await context.EmployeeEmploymentProfiles.AnyAsync(
            existing =>
                existing.UserId == profile.UserId &&
                (!profile.EffectiveTo.HasValue ||
                 existing.EffectiveFrom <= profile.EffectiveTo.Value) &&
                (!existing.EffectiveTo.HasValue ||
                 existing.EffectiveTo >= profile.EffectiveFrom));
        if (overlapping)
        {
            throw new LeaveOverlapException(
                "The employee has an employment profile overlapping those inclusive dates.");
        }

        context.EmployeeEmploymentProfiles.Add(profile);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<EmployeeLeaveMetadata>>
        GetEmployeeMetadataAsync(int employeeUserId, DateOnly asOfDate)
    {
        var employee = await context.Users
            .AsNoTracking()
            .Include(x => x.Department)
            .SingleOrDefaultAsync(x => x.Id == employeeUserId);
        if (employee is null)
        {
            throw new LeaveNotFoundException("Employee");
        }

        var profiles = await context.EmployeeEmploymentProfiles
            .AsNoTracking()
            .Include(x => x.Designation)
            .Where(x => x.UserId == employeeUserId)
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync();
        if (profiles.Count == 0)
        {
            return
            [
                new EmployeeLeaveMetadata(
                    employee.Id,
                    employee.FullName,
                    employee.Role,
                    employee.DepartmentId,
                    employee.Department?.Name,
                    null,
                    null,
                    null,
                    null,
                    asOfDate,
                    null)
            ];
        }

        return profiles.Select(profile =>
            new EmployeeLeaveMetadata(
                employee.Id,
                employee.FullName,
                employee.Role,
                employee.DepartmentId,
                employee.Department?.Name,
                profile.DesignationId,
                profile.Designation?.Name,
                profile.EmploymentType,
                profile.HireDate,
                profile.EffectiveFrom,
                profile.EffectiveTo)).ToList();
    }

    public async Task<LeavePolicyResolutionData?> GetPolicyResolutionDataAsync(
        int employeeUserId,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        if (rangeEnd < rangeStart)
        {
            throw new LeaveInvalidInputException(
                "The resolution end date cannot precede its start date.");
        }

        var employee = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == employeeUserId);
        if (employee is null)
        {
            return null;
        }

        var profiles = await context.EmployeeEmploymentProfiles
            .AsNoTracking()
            .Where(x =>
                x.UserId == employeeUserId &&
                x.EffectiveFrom <= rangeEnd &&
                (x.EffectiveTo == null || x.EffectiveTo >= rangeStart))
            .ToListAsync();
        var assignments = await context.LeavePolicyAssignments
            .AsNoTracking()
            .Where(x =>
                x.UserId == employeeUserId &&
                x.EffectiveFrom <= rangeEnd &&
                (x.EffectiveTo == null || x.EffectiveTo >= rangeStart))
            .ToListAsync();
        var policies = await context.LeavePolicies
            .AsNoTracking()
            .Include(x => x.Entitlements)
            .Where(x =>
                x.Status != LeavePolicyStatus.Draft &&
                x.EffectiveFrom <= rangeEnd &&
                (x.EffectiveTo == null || x.EffectiveTo >= rangeStart))
            .ToListAsync();
        var scheduleIds = policies
            .Where(policy =>
                policy.LeaveWorkScheduleId.HasValue)
            .Select(policy => policy.LeaveWorkScheduleId!.Value)
            .Distinct()
            .ToArray();
        var scheduleDays = await context.LeaveWorkScheduleDays
            .AsNoTracking()
            .Where(day =>
                scheduleIds.Contains(day.LeaveWorkScheduleId) &&
                day.IsWorkingDay)
            .ToListAsync();
        var workingDaysBySchedule = scheduleDays
            .GroupBy(day => day.LeaveWorkScheduleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<DayOfWeek>)group
                    .Select(day => day.DayOfWeek)
                    .ToHashSet());
        var holidayCalendarIds = policies
            .Where(policy =>
                policy.LeaveHolidayCalendarId.HasValue)
            .Select(policy => policy.LeaveHolidayCalendarId!.Value)
            .Distinct()
            .ToArray();
        var holidayRows = await context.LeaveHolidays
            .AsNoTracking()
            .Where(holiday =>
                holidayCalendarIds.Contains(
                    holiday.LeaveHolidayCalendarId) &&
                holiday.Date >= rangeStart &&
                holiday.Date <= rangeEnd)
            .ToListAsync();
        var holidaysByCalendar = holidayCalendarIds.ToDictionary(
            calendarId => calendarId,
            calendarId => (IReadOnlySet<DateOnly>)holidayRows
                .Where(holiday =>
                    holiday.LeaveHolidayCalendarId == calendarId)
                .Select(holiday => holiday.Date)
                .ToHashSet());

        return new LeavePolicyResolutionData(
            employee,
            profiles,
            assignments,
            policies,
            workingDaysBySchedule,
            holidaysByCalendar);
    }

    public async Task<int?> GetEnabledLeaveTypeIdAsync(string codeOrName)
    {
        var normalized = codeOrName.Trim();
        var matches = await context.LeaveTypes
            .Where(x =>
                x.IsEnabled &&
                (x.Code == normalized.ToUpper() ||
                 x.DisplayName == normalized))
            .Select(x => x.Id)
            .ToListAsync();
        if (matches.Count > 1)
        {
            throw new LeaveConfigurationException(
                "The leave type name is ambiguous; use its unique code.");
        }

        return matches.SingleOrDefault();
    }

    private async Task ValidatePolicyReferencesAsync(LeavePolicy policy)
    {
        if (policy.DepartmentId is int departmentId &&
            !await context.Departments.AnyAsync(
                x => x.Id == departmentId && x.IsActive))
        {
            throw new LeaveInvalidInputException(
                "The policy department does not exist or is disabled.");
        }

        if (policy.DesignationId is int designationId &&
            !await context.Designations.AnyAsync(
                x => x.Id == designationId && x.IsEnabled))
        {
            throw new LeaveInvalidInputException(
                "The policy designation does not exist or is disabled.");
        }

        if (policy.LeaveWorkScheduleId is int scheduleId &&
            !await context.LeaveWorkSchedules.AnyAsync(
                x => x.Id == scheduleId && x.IsEnabled))
        {
            throw new LeaveInvalidInputException(
                "The policy work schedule does not exist or is disabled.");
        }

        if (policy.DayCalculationMethod ==
                LeaveDayCalculationMethod.WorkingDays &&
            await context.LeaveWorkScheduleDays
                .Where(x => x.LeaveWorkScheduleId ==
                    policy.LeaveWorkScheduleId)
                .Select(x => x.DayOfWeek)
                .Distinct()
                .CountAsync() != 7)
        {
            throw new LeaveConfigurationException(
                "The applicable work schedule must define all seven days of the week.");
        }

        if (policy.LeaveHolidayCalendarId is int holidayCalendarId &&
            !await context.LeaveHolidayCalendars.AnyAsync(
                x => x.Id == holidayCalendarId && x.IsEnabled))
        {
            throw new LeaveInvalidInputException(
                "The policy holiday calendar does not exist or is disabled.");
        }

        if (policy.LeavePayrollCalendarId is int payrollCalendarId &&
            !await context.LeavePayrollCalendars.AnyAsync(
                x => x.Id == payrollCalendarId && x.IsEnabled))
        {
            throw new LeaveInvalidInputException(
                "The policy payroll calendar does not exist or is disabled.");
        }

        if (policy.LeavePayrollCalendarId is int enabledCalendarId)
        {
            var timeZoneId = await context.LeavePayrollCalendars
                .Where(x => x.Id == enabledCalendarId)
                .Select(x => x.TimeZoneId)
                .SingleAsync();
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new LeaveConfigurationException(
                    "The payroll calendar time zone is not supported by this server.");
            }
            catch (InvalidTimeZoneException)
            {
                throw new LeaveConfigurationException(
                    "The payroll calendar time zone is invalid.");
            }
        }
    }

    private async Task ValidatePolicyEntitlementsAsync(LeavePolicy policy)
    {
        var enabledLeaveTypeIds = await context.LeaveTypes
            .Where(x => x.IsEnabled)
            .Select(x => x.Id)
            .ToListAsync();
        var entitlementIds = policy.Entitlements
            .Select(x => x.LeaveTypeId)
            .ToList();
        if (enabledLeaveTypeIds.Any(id =>
                entitlementIds.Count(entitlementId => entitlementId == id) != 1))
        {
            throw new LeaveConfigurationException(
                "A policy must define exactly one entitlement for every enabled leave type.");
        }

        if (policy.Entitlements.Any(x =>
                x.AnnualEntitlement < 0 ||
                x.AnnualEntitlement > 9_999_999.99m ||
                x.AnnualEntitlement !=
                    decimal.Round(
                        x.AnnualEntitlement,
                        2,
                        MidpointRounding.AwayFromZero)))
        {
            throw new LeaveConfigurationException(
                "A policy contains an invalid leave entitlement.");
        }

        if (policy.LeavePayrollCalendarId is int calendarId)
        {
            var calendar = await context.LeavePayrollCalendars
                .SingleAsync(x => x.Id == calendarId);
            var periods = await context.LeavePayPeriods
                .Where(x => x.LeavePayrollCalendarId == calendarId)
                .OrderBy(x => x.StartDate)
                .ToListAsync();
            if (periods.Count == 0 ||
                periods.GroupBy(x => x.EntitlementYear)
                    .Any(group => group.Count() !=
                        calendar.ExpectedPeriodsInEntitlementYear) ||
                HasOverlappingPeriods(periods))
            {
                throw new LeaveConfigurationException(
                    "The payroll calendar must contain the configured number of non-overlapping periods in each entitlement year.");
            }
        }
    }

    private async Task ValidatePolicyPublicationRangeAsync(LeavePolicy policy)
    {
        var existing = await context.LeavePolicies
            .Where(x =>
                (x.Status == LeavePolicyStatus.Published ||
                 x.Status == LeavePolicyStatus.Superseded) &&
                x.Scope == policy.Scope &&
                x.DepartmentId == policy.DepartmentId &&
                x.DesignationId == policy.DesignationId &&
                x.EmploymentType == policy.EmploymentType)
            .ToListAsync();

        foreach (var other in existing)
        {
            if (string.Equals(other.Code, policy.Code, StringComparison.Ordinal))
            {
                var latest = existing
                    .Where(x => x.Code == policy.Code)
                    .OrderByDescending(x => x.Version)
                    .First();
                if (policy.Version != latest.Version + 1 ||
                    policy.EffectiveFrom <= latest.EffectiveFrom)
                {
                    throw new LeaveOverlapException(
                        "A new policy version must follow the latest version and have a later effective date.");
                }

                continue;
            }

            if (Overlaps(
                    other.EffectiveFrom,
                    other.EffectiveTo,
                    policy.EffectiveFrom,
                    policy.EffectiveTo))
            {
                throw new LeaveOverlapException(
                    "Another published policy for the same scope overlaps those inclusive effective dates.");
            }
        }
    }

    private void AddEntitlements(
        LeavePolicy policy,
        IReadOnlyList<LeavePolicyEntitlementInput> entitlements)
    {
        foreach (var item in entitlements)
        {
            policy.AddEntitlement(new LeavePolicyEntitlement(
                policy.Id,
                item.LeaveTypeId,
                item.AnnualEntitlement));
        }

        context.LeavePolicyEntitlements.AddRange(policy.Entitlements);
    }

    private static LeavePolicy ClonePolicy(
        LeavePolicy source,
        int version,
        DateOnly effectiveFrom)
    {
        return new LeavePolicy(
            source.Code,
            source.Name,
            version,
            source.Scope,
            source.DepartmentId,
            source.DesignationId,
            source.EmploymentType,
            effectiveFrom,
            null,
            source.DayCalculationMethod,
            source.LeaveWorkScheduleId,
            source.LeaveHolidayCalendarId,
            source.AllowsHalfDays,
            source.HalfDayUnits,
            source.IsAccrualEnabled,
            source.AccrualFrequency,
            source.AllowsCarryForward,
            source.CarryForwardCap,
            source.CarryForwardExpiryMonths,
            source.ProbationPeriodDays,
            source.AllowsLeaveDuringProbation,
            source.AllowsNegativeBalance,
            source.LeavePayrollCalendarId);
    }

    private static bool Overlaps(
        DateOnly existingStart,
        DateOnly? existingEnd,
        DateOnly requestedStart,
        DateOnly? requestedEnd)
    {
        var existingLast = existingEnd ?? DateOnly.MaxValue;
        var requestedLast = requestedEnd ?? DateOnly.MaxValue;
        return existingStart <= requestedLast &&
            requestedStart <= existingLast;
    }

    private static bool HasOverlappingPeriods(
        IReadOnlyList<LeavePayPeriod> periods)
    {
        for (var index = 1; index < periods.Count; index++)
        {
            if (periods[index].StartDate <= periods[index - 1].EndDate)
            {
                return true;
            }
        }

        return false;
    }
}
