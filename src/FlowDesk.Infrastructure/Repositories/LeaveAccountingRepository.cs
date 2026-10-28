using System.Data;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class LeaveAccountingRepository(
    AppDbContext context) : ILeaveAccountingRepository
{
    public async Task<IReadOnlyList<LeaveType>> GetLeaveTypesAsync(
        bool enabledOnly)
    {
        var query = context.LeaveTypes.AsNoTracking();
        if (enabledOnly)
        {
            query = query.Where(x => x.IsEnabled);
        }

        return await query.OrderBy(x => x.DisplayName).ToListAsync();
    }

    public async Task<IReadOnlyList<LeaveType>> GetEnabledLeaveTypesAsync()
    {
        return await GetLeaveTypesAsync(enabledOnly: true);
    }

    public async Task<LeaveType> CreateLeaveTypeAsync(LeaveType leaveType)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (await context.LeaveTypes.AnyAsync(x => x.Code == leaveType.Code))
        {
            throw new LeaveInvalidInputException(
                "A leave type with this code already exists.");
        }

        context.LeaveTypes.Add(leaveType);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return leaveType;
    }

    public async Task<LeaveType> UpdateLeaveTypeAsync(
        int leaveTypeId,
        string displayName,
        bool isPaid,
        bool isEnabled)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var leaveType = await context.LeaveTypes
            .SingleOrDefaultAsync(x => x.Id == leaveTypeId)
            ?? throw new LeaveNotFoundException("Leave type");
        leaveType.Update(displayName, isPaid, isEnabled);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return leaveType;
    }

    public async Task<IReadOnlyList<LeaveBalanceSummary>> GetBalancesAsync(
        int employeeUserId,
        DateOnly asOfDate)
    {
        var balances = await GetBalancesForEmployeesAsync(
            [employeeUserId],
            asOfDate);
        return balances.GetValueOrDefault(employeeUserId) ?? [];
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<LeaveBalanceSummary>>>
        GetBalancesForEmployeesAsync(
            IReadOnlyList<int> employeeUserIds,
            DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(employeeUserIds);
        if (employeeUserIds.Any(id => id <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(employeeUserIds));
        }

        var requestedIds = employeeUserIds.Distinct().ToArray();
        var types = await context.LeaveTypes
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync();
        var ledger = await context.LeaveBalanceLedgerEntries
            .AsNoTracking()
            .Where(x =>
                requestedIds.Contains(x.EmployeeUserId) &&
                x.EffectiveDate <= asOfDate)
            .ToListAsync();
        var ledgerIds = ledger.Select(entry => entry.Id).ToList();
        var sourceAllocations = await context.LeaveBalanceAllocations
            .AsNoTracking()
            .Where(allocation =>
                ledgerIds.Contains(allocation.SourceEntryId) &&
                allocation.LedgerEntry.EffectiveDate <= asOfDate)
            .Select(allocation => new
            {
                allocation.SourceEntryId,
                allocation.Units,
                EntryType = allocation.LedgerEntry.EntryType,
                EntryUnits = allocation.LedgerEntry.Units
            })
            .ToListAsync();

        return requestedIds.ToDictionary(
            employeeUserId => employeeUserId,
            employeeUserId => (IReadOnlyList<LeaveBalanceSummary>)types.Select(type =>
        {
            var entries = ledger
                .Where(entry =>
                    entry.EmployeeUserId == employeeUserId &&
                    entry.LeaveTypeId == type.Id)
                .ToList();
            var accrualIds = entries
                .Where(entry =>
                    entry.EntryType == LeaveLedgerEntryType.Accrual)
                .Select(entry => entry.Id)
                .ToHashSet();
            var sourceEntries = entries
                .Where(entry =>
                    entry.EntryType == LeaveLedgerEntryType.Accrual ||
                    entry.EntryType == LeaveLedgerEntryType.CarryForward ||
                    (entry.EntryType == LeaveLedgerEntryType.Adjustment &&
                     entry.Units > 0))
                .ToList();
            var transferred = sourceAllocations
                .Where(allocation =>
                    accrualIds.Contains(allocation.SourceEntryId) &&
                    allocation.EntryType == LeaveLedgerEntryType.CarryForward)
                .Sum(allocation => allocation.Units);
            var unpostedExpired = sourceEntries
                .Where(entry =>
                    entry.ExpiresOn.HasValue &&
                    entry.ExpiresOn.Value < asOfDate)
                .Sum(entry =>
                {
                    var allocations = sourceAllocations
                        .Where(allocation =>
                            allocation.SourceEntryId == entry.Id)
                        .ToList();
                    var consumed = allocations
                        .Where(allocation =>
                            allocation.EntryType is LeaveLedgerEntryType.Used or
                                LeaveLedgerEntryType.ScheduledCommitment or
                                LeaveLedgerEntryType.Expiry or
                                LeaveLedgerEntryType.CarryForward ||
                            (allocation.EntryType ==
                                LeaveLedgerEntryType.Adjustment &&
                             allocation.EntryUnits < 0))
                        .Sum(allocation => allocation.Units);
                    var releasedScheduled = allocations
                        .Where(allocation =>
                            allocation.EntryType ==
                                LeaveLedgerEntryType.ScheduledRelease)
                        .Sum(allocation => allocation.Units);
                    var reserved = allocations
                        .Where(allocation =>
                            allocation.EntryType ==
                                LeaveLedgerEntryType.Reservation)
                        .Sum(allocation => allocation.Units) -
                        allocations
                            .Where(allocation =>
                                allocation.EntryType ==
                                    LeaveLedgerEntryType.ReservationRelease)
                            .Sum(allocation => allocation.Units);
                    return Math.Max(
                        0m,
                        entry.Units - consumed + releasedScheduled - reserved);
                });
            var earned = Sum(entries, LeaveLedgerEntryType.Accrual) -
                transferred;
            var adjustments =
                Sum(entries, LeaveLedgerEntryType.Adjustment) +
                Sum(entries, LeaveLedgerEntryType.Reversal);
            var carryForward = Sum(entries, LeaveLedgerEntryType.CarryForward);
            var used = Sum(entries, LeaveLedgerEntryType.Used) +
                Sum(entries, LeaveLedgerEntryType.ScheduledToUsed);
            var pending = Sum(entries, LeaveLedgerEntryType.Reservation) -
                Sum(entries, LeaveLedgerEntryType.ReservationRelease);
            var scheduled = Sum(entries, LeaveLedgerEntryType.ScheduledCommitment) -
                Sum(entries, LeaveLedgerEntryType.ScheduledRelease) -
                Sum(entries, LeaveLedgerEntryType.ScheduledToUsed);
            var expired = Sum(entries, LeaveLedgerEntryType.Expiry);
            var available = earned + adjustments + carryForward -
                used - pending - scheduled - expired - unpostedExpired;
            return new LeaveBalanceSummary(
                employeeUserId,
                type.Id,
                type.Code,
                type.DisplayName,
                earned,
                adjustments,
                carryForward,
                used,
                pending,
                scheduled,
                expired,
                available);
        }).ToList());
    }

    public async Task<IReadOnlyList<LeaveLedgerHistoryItem>> GetLedgerHistoryAsync(
        int employeeUserId,
        int? leaveTypeId,
        DateOnly? throughDate)
    {
        var query = context.LeaveBalanceLedgerEntries
            .AsNoTracking()
            .Include(x => x.LeaveType)
            .Where(x => x.EmployeeUserId == employeeUserId);
        if (leaveTypeId.HasValue)
        {
            query = query.Where(x => x.LeaveTypeId == leaveTypeId.Value);
        }

        if (throughDate.HasValue)
        {
            query = query.Where(x => x.EffectiveDate <= throughDate.Value);
        }

        var entries = await query
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
        return entries.Select(entry => new LeaveLedgerHistoryItem(
            entry.Id,
            entry.LeaveTypeId,
            entry.LeaveType.Code,
            entry.LeaveType.DisplayName,
            entry.EntryType,
            entry.Units,
            entry.EffectiveDate,
            entry.CreatedAtUtc,
            entry.ExpiresOn,
            entry.LeavePolicyId,
            entry.RequestId,
            entry.Reason,
            entry.ActorUserId)).ToList();
    }

    public async Task<IReadOnlyList<LeaveBalanceSourceView>> GetAvailableSourcesAsync(
        int employeeUserId,
        int leaveTypeId,
        DateOnly businessDate)
    {
        var credits = await context.LeaveBalanceLedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.EmployeeUserId == employeeUserId &&
                entry.LeaveTypeId == leaveTypeId &&
                entry.EffectiveDate <= businessDate &&
                (entry.EntryType == LeaveLedgerEntryType.Accrual ||
                 entry.EntryType == LeaveLedgerEntryType.CarryForward ||
                 (entry.EntryType == LeaveLedgerEntryType.Adjustment &&
                  entry.Units > 0)))
            .ToListAsync();
        if (credits.Count == 0)
        {
            return [];
        }

        var sourceIds = credits.Select(entry => entry.Id).ToList();
        var allocations = await context.LeaveBalanceAllocations
            .AsNoTracking()
            .Where(allocation =>
                sourceIds.Contains(allocation.SourceEntryId) &&
                allocation.LedgerEntry.EffectiveDate <= businessDate)
            .Select(allocation => new
            {
                allocation.SourceEntryId,
                allocation.Units,
                EntryType = allocation.LedgerEntry.EntryType,
                EntryUnits = allocation.LedgerEntry.Units
            })
            .ToListAsync();

        return credits.Select(source =>
        {
            var sourceAllocations = allocations
                .Where(allocation =>
                    allocation.SourceEntryId == source.Id)
                .ToList();
            var consumed = sourceAllocations
                .Where(allocation =>
                    allocation.EntryType is LeaveLedgerEntryType.Used or
                        LeaveLedgerEntryType.ScheduledCommitment or
                        LeaveLedgerEntryType.Expiry or
                        LeaveLedgerEntryType.CarryForward ||
                    (allocation.EntryType == LeaveLedgerEntryType.Adjustment &&
                     allocation.EntryUnits < 0))
                .Sum(allocation => allocation.Units);
            var releasedScheduled = sourceAllocations
                .Where(allocation =>
                    allocation.EntryType == LeaveLedgerEntryType.ScheduledRelease)
                .Sum(allocation => allocation.Units);
            var pending = sourceAllocations
                .Where(allocation =>
                    allocation.EntryType == LeaveLedgerEntryType.Reservation)
                .Sum(allocation => allocation.Units) -
                sourceAllocations
                    .Where(allocation =>
                        allocation.EntryType ==
                            LeaveLedgerEntryType.ReservationRelease)
                    .Sum(allocation => allocation.Units);
            consumed -= releasedScheduled;
            return new LeaveBalanceSourceView(
                source.Id,
                source.EntryType == LeaveLedgerEntryType.CarryForward
                    ? LeaveBalanceSourceKind.CarryForward
                    : LeaveBalanceSourceKind.CurrentCycleEarned,
                source.Units,
                Math.Max(consumed, 0m),
                Math.Max(pending, 0m),
                source.EffectiveDate,
                source.ExpiresOn);
        }).ToList();
    }

    public async Task<bool> TryAppendLedgerWriteAsync(LeaveLedgerWrite write)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var exists = await context.LeaveBalanceLedgerEntries.AnyAsync(
            x => x.IdempotencyKey == write.Entry.IdempotencyKey);
        if (exists)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await ValidateAllocationsAsync(write);
        context.LeaveBalanceLedgerEntries.Add(write.Entry);
        await context.SaveChangesAsync();
        foreach (var allocation in write.Allocations)
        {
            context.LeaveBalanceAllocations.Add(
                new LeaveBalanceAllocation(
                    write.Entry.Id,
                    allocation.SourceEntryId,
                    allocation.LeaveDate,
                    allocation.Units));
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<int> ProcessDueScheduledCommitmentsAsync(
        DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The processing timestamp must be UTC.",
                nameof(nowUtc));
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        var utcDate = DateOnly.FromDateTime(nowUtc);
        var candidates = await context.LeaveBalanceLedgerEntries
            .Include(entry => entry.LeavePolicy)
                .ThenInclude(policy => policy!.LeavePayrollCalendar)
            .Where(entry =>
                entry.EntryType ==
                    LeaveLedgerEntryType.ScheduledCommitment &&
                entry.LeaveDate.HasValue &&
                entry.EffectiveDate <= utcDate.AddDays(1) &&
                entry.LeaveDate <= utcDate.AddDays(1))
            .ToListAsync();
        var converted = 0;

        foreach (var commitment in candidates)
        {
            var timeZoneId = commitment.LeavePolicy?
                .LeavePayrollCalendar?.TimeZoneId;
            var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var businessDate = LeaveAccrualCalculator.GetBusinessDate(
                nowUtc,
                timeZone);
            if (commitment.LeaveDate > businessDate)
            {
                continue;
            }

            var leaveDate = commitment.LeaveDate!.Value;
            var key =
                $"leave-request:{commitment.RequestId}:scheduled-to-used:{leaveDate:yyyyMMdd}";
            if (await context.LeaveBalanceLedgerEntries.AnyAsync(
                    entry => entry.IdempotencyKey == key))
            {
                continue;
            }

            context.LeaveBalanceLedgerEntries.Add(
                new LeaveBalanceLedgerEntry(
                    commitment.EmployeeUserId,
                    commitment.LeaveTypeId,
                    commitment.LeavePolicyId,
                    commitment.RequestId,
                    null,
                    LeaveLedgerEntryType.ScheduledToUsed,
                    leaveDate,
                    commitment.Units,
                    key,
                    nowUtc,
                    leaveDate: leaveDate));
            converted++;
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return converted;
    }

    public async Task<int> ProcessDueCarryForwardAndExpiryAsync(
        DateTime nowUtc,
        string businessTimeZoneId)
    {
        EnsureUtc(nowUtc);
        var businessDate = LeaveAccrualCalculator.GetBusinessDate(
            nowUtc,
            GetTimeZone(businessTimeZoneId));
        var employees = await context.Users
            .AsNoTracking()
            .Where(user => user.IsActive && user.Role != UserRoles.Admin)
            .OrderBy(user => user.Id)
            .ToListAsync();
        var types = await context.LeaveTypes
            .AsNoTracking()
            .Where(type => type.IsEnabled)
            .ToListAsync();
        var policies = await context.LeavePolicies
            .AsNoTracking()
            .Include(policy => policy.Entitlements)
            .Include(policy => policy.LeavePayrollCalendar)
            .Where(policy =>
                policy.Status != LeavePolicyStatus.Draft)
            .ToListAsync();
        var created = 0;

        var expiringEntries = await context.LeaveBalanceLedgerEntries
            .AsNoTracking()
            .Where(entry =>
                (entry.EntryType == LeaveLedgerEntryType.Accrual ||
                 entry.EntryType == LeaveLedgerEntryType.CarryForward ||
                 (entry.EntryType == LeaveLedgerEntryType.Adjustment &&
                  entry.Units > 0)) &&
                entry.ExpiresOn.HasValue &&
                entry.ExpiresOn.Value < businessDate)
            .OrderBy(entry => entry.ExpiresOn)
            .ToListAsync();
        foreach (var source in expiringEntries)
        {
            var available = (await GetAvailableSourcesAsync(
                    source.EmployeeUserId,
                    source.LeaveTypeId,
                    businessDate))
                .SingleOrDefault(view => view.SourceId == source.Id);
            var unitsToExpire = available is null
                ? 0m
                : available.GrantedUnits -
                    available.ConsumedUnits -
                    available.ReservedUnits;
            if (unitsToExpire <= 0)
            {
                continue;
            }

            var expiryDate = source.ExpiresOn!.Value;
            if (await AppendExpiryAsync(
                    source.EmployeeUserId,
                    source.LeaveTypeId,
                    source.LeavePolicyId,
                    source.Id,
                    expiryDate,
                    unitsToExpire,
                    nowUtc))
            {
                created++;
            }
        }

        foreach (var employee in employees)
        {
            var data = await GetAccrualResolutionDataAsync(
                employee,
                policies);
            var firstPolicyDate = policies.Count == 0
                ? businessDate
                : policies.Min(policy => policy.EffectiveFrom);
            var hireDate = data.EmploymentProfiles
                .Where(profile => profile.UserId == employee.Id)
                .Select(profile => profile.HireDate)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .DefaultIfEmpty(firstPolicyDate)
                .Min();
            for (var year = Math.Max(hireDate.Year, firstPolicyDate.Year);
                 year < businessDate.Year;
                 year++)
            {
                var cycleStart = new DateOnly(year, 1, 1);
                var cycleEnd = new DateOnly(year, 12, 31);
                if (cycleEnd >= businessDate)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    var profile = data.EmploymentProfiles.SingleOrDefault(
                        item => item.UserId == employee.Id &&
                            IsEffective(
                                item.EffectiveFrom,
                                item.EffectiveTo,
                                cycleEnd));
                    if (!HasApplicablePolicy(
                            data,
                            profile,
                            cycleEnd))
                    {
                        continue;
                    }

                    var resolution = LeavePolicyResolver.Resolve(
                        employee,
                        data.EmploymentProfiles,
                        data.Assignments,
                        data.Policies,
                        type.Id,
                        cycleEnd);
                    var policy = resolution.Policy;
                    var eligibleSourceIds = await context
                        .LeaveBalanceLedgerEntries
                        .AsNoTracking()
                        .Where(entry =>
                            entry.EmployeeUserId == employee.Id &&
                            entry.LeaveTypeId == type.Id &&
                            entry.EntryType == LeaveLedgerEntryType.Accrual &&
                            entry.EffectiveDate >= cycleStart &&
                            entry.EffectiveDate <= cycleEnd)
                        .Select(entry => entry.Id)
                        .ToListAsync();
                    if (eligibleSourceIds.Count == 0)
                    {
                        continue;
                    }

                    var sources = (await GetAvailableSourcesAsync(
                            employee.Id,
                            type.Id,
                            cycleEnd))
                        .Where(source =>
                            eligibleSourceIds.Contains(source.SourceId))
                        .OrderBy(source => source.EarnedOn)
                        .ThenBy(source => source.SourceId)
                        .ToList();
                    var cap = policy.CarryForwardCap ??
                        (policy.AllowsCarryForward ? decimal.MaxValue : 0m);
                    var existingCarryForward = await context
                        .LeaveBalanceLedgerEntries
                        .Where(entry =>
                            entry.EmployeeUserId == employee.Id &&
                            entry.LeaveTypeId == type.Id &&
                            entry.LeavePolicyId == policy.Id &&
                            entry.EntryType ==
                                LeaveLedgerEntryType.CarryForward &&
                            entry.EffectiveDate == cycleEnd.AddDays(1))
                        .SumAsync(entry => (decimal?)entry.Units) ?? 0m;
                    var remainingCap = Math.Max(
                        0m,
                        cap - existingCarryForward);
                    var expiryDate = policy.CarryForwardExpiryMonths is int months
                        ? LeaveAllocationPlanner.CalculateExpiryDate(
                            cycleEnd,
                            months)
                        : (DateOnly?)null;
                    foreach (var source in sources)
                    {
                        var carryForwardAtBoundary =
                            await GetAllocatedUnitsAsync(
                                source.SourceId,
                                LeaveLedgerEntryType.CarryForward,
                                cycleEnd.AddDays(1));
                        var expiredAtBoundary =
                            await GetAllocatedUnitsAsync(
                                source.SourceId,
                                LeaveLedgerEntryType.Expiry,
                                cycleEnd.AddDays(1));
                        var availableUnits = Math.Max(
                            0m,
                            source.GrantedUnits -
                            source.ConsumedUnits -
                            source.ReservedUnits -
                            carryForwardAtBoundary -
                            expiredAtBoundary);
                        var transferUnits = Math.Min(
                            availableUnits,
                            remainingCap);
                        if (transferUnits > 0)
                        {
                            var transferredForSource =
                                await GetAllocatedUnitsAsync(
                                    source.SourceId,
                                    LeaveLedgerEntryType.CarryForward);
                            var carryForward = new LeaveBalanceLedgerEntry(
                                employee.Id,
                                type.Id,
                                policy.Id,
                                null,
                                null,
                                LeaveLedgerEntryType.CarryForward,
                                cycleEnd.AddDays(1),
                                transferUnits,
                                $"leave-carry-forward:{employee.Id}:{type.Id}:{policy.Id}:{policy.Version}:{year}:{source.SourceId}:{(transferredForSource + transferUnits).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}",
                                nowUtc,
                                expiresOn: expiryDate);
                            var transfer = new LeaveLedgerWrite(
                                carryForward,
                                [
                                    new LeaveLedgerAllocationInput(
                                        source.SourceId,
                                        cycleEnd.AddDays(1),
                                        transferUnits)
                                ]);
                            if (await TryAppendLedgerWriteAsync(transfer))
                            {
                                created++;
                                remainingCap -= transferUnits;
                            }
                            else
                            {
                                existingCarryForward = await context
                                    .LeaveBalanceLedgerEntries
                                    .Where(entry =>
                                        entry.EmployeeUserId == employee.Id &&
                                        entry.LeaveTypeId == type.Id &&
                                        entry.LeavePolicyId == policy.Id &&
                                        entry.EntryType ==
                                            LeaveLedgerEntryType.CarryForward &&
                                        entry.EffectiveDate ==
                                            cycleEnd.AddDays(1))
                                    .SumAsync(entry =>
                                        (decimal?)entry.Units) ?? 0m;
                                remainingCap = Math.Max(
                                    0m,
                                    cap - existingCarryForward);
                            }
                        }

                        var availableAfterTransfer = Math.Max(
                            0m,
                            availableUnits - transferUnits);
                        if (availableAfterTransfer > 0 &&
                            await AppendExpiryAsync(
                                employee.Id,
                                type.Id,
                                policy.Id,
                                source.SourceId,
                                cycleEnd,
                                availableAfterTransfer,
                                nowUtc))
                        {
                            created++;
                        }
                    }
                }
            }
        }

        return created;
    }

    public async Task<int> ProcessDueAccrualsAsync(
        DateTime nowUtc,
        string businessTimeZoneId)
    {
        EnsureUtc(nowUtc);
        var businessTimeZone = GetTimeZone(businessTimeZoneId);
        var businessDate = LeaveAccrualCalculator.GetBusinessDate(
            nowUtc,
            businessTimeZone);
        var employees = await context.Users
            .AsNoTracking()
            .Where(user => user.IsActive && user.Role != UserRoles.Admin)
            .OrderBy(user => user.Id)
            .ToListAsync();
        var leaveTypes = await context.LeaveTypes
            .AsNoTracking()
            .Where(type => type.IsEnabled)
            .ToListAsync();
        var policies = await context.LeavePolicies
            .AsNoTracking()
            .Include(policy => policy.Entitlements)
            .Include(policy => policy.LeavePayrollCalendar)
            .Where(policy =>
                policy.Status != LeavePolicyStatus.Draft)
            .ToListAsync();
        var payPeriods = await context.LeavePayPeriods
            .AsNoTracking()
            .ToListAsync();
        var created = 0;
        var monthlyUnrounded =
            new Dictionary<AccrualCycleKey, decimal>();
        var payPeriodUnrounded =
            new Dictionary<AccrualCycleKey, decimal>();

        foreach (var employee in employees)
        {
            var resolutionData = await GetAccrualResolutionDataAsync(
                employee,
                policies);
            var firstPolicyDate = policies.Count == 0
                ? businessDate
                : policies.Min(policy => policy.EffectiveFrom);
            var employmentStart = resolutionData.EmploymentProfiles
                .Where(profile => profile.UserId == employee.Id)
                .Select(profile => profile.HireDate)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .DefaultIfEmpty(firstPolicyDate)
                .Min();
            var startDate = employmentStart > firstPolicyDate
                ? employmentStart
                : firstPolicyDate;
            if (startDate > businessDate)
            {
                continue;
            }

            foreach (var leaveType in leaveTypes)
            {
                for (var year = startDate.Year;
                     year <= businessDate.Year;
                     year++)
                {
                    var periodStart = new DateOnly(year, 1, 1);
                    var periodEnd = new DateOnly(year, 12, 31);
                    var annualDays = ResolveAccrualDays(
                        resolutionData,
                        leaveType.Id,
                        periodStart,
                        periodEnd);
                    foreach (var group in annualDays)
                    {
                        var policy = group.Policy;
                        if (!policy.IsAccrualEnabled ||
                            policy.AccrualFrequency !=
                                LeaveAccrualFrequency.Annually ||
                            group.Dates[0] > businessDate)
                        {
                            continue;
                        }

                        var entitlement = RequireEntitlement(
                            policy,
                            leaveType.Id);
                        var grant = LeaveAccrualCalculator.RoundUnits(
                            entitlement.AnnualEntitlement *
                            group.Dates.Count /
                            (periodEnd.DayNumber - periodStart.DayNumber + 1));
                        created += await AppendAccrualIfNewAsync(
                            employee.Id,
                            leaveType.Id,
                            policy,
                            periodStart,
                            periodEnd,
                            group.Dates[0],
                            grant,
                            null,
                            nowUtc);
                    }

                    var firstMonth = new DateOnly(year, 1, 1);
                    var lastMonth = year == businessDate.Year
                        ? businessDate.Month
                        : 12;
                    for (var month = 1; month <= lastMonth; month++)
                    {
                        var monthStart = new DateOnly(year, month, 1);
                        if (monthStart.AddMonths(1).AddDays(-1) >
                            businessDate)
                        {
                            break;
                        }

                        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                        var monthlyDays = ResolveAccrualDays(
                            resolutionData,
                            leaveType.Id,
                            monthStart,
                            monthEnd);
                        foreach (var group in monthlyDays)
                        {
                            var policy = group.Policy;
                            if (!policy.IsAccrualEnabled ||
                                policy.AccrualFrequency !=
                                    LeaveAccrualFrequency.Monthly)
                            {
                                continue;
                            }

                            var entitlement = RequireEntitlement(
                                policy,
                                leaveType.Id);
                            var cumulativeKey = new AccrualCycleKey(
                                employee.Id,
                                leaveType.Id,
                                policy.Id,
                                year);
                            var raw = entitlement.AnnualEntitlement / 12m *
                                group.Dates.Count /
                                DateTime.DaysInMonth(year, month);
                            monthlyUnrounded.TryGetValue(
                                cumulativeKey,
                                out var cumulative);
                            cumulative += raw;
                            monthlyUnrounded[cumulativeKey] = cumulative;

                            var key = LeaveAccrualCalculator
                                .CreateIdempotencyKey(
                                    employee.Id,
                                    leaveType.Id,
                                    policy.Id,
                                    policy.Version,
                                    monthStart,
                                    monthEnd,
                                    null);
                            if (await HasLedgerKeyAsync(key))
                            {
                                continue;
                            }

                            var previouslyCredited = await context
                                .LeaveBalanceLedgerEntries
                                .Where(entry =>
                                    entry.EmployeeUserId == employee.Id &&
                                    entry.LeaveTypeId == leaveType.Id &&
                                    entry.LeavePolicyId == policy.Id &&
                                    entry.EntryType ==
                                        LeaveLedgerEntryType.Accrual &&
                                    entry.EffectiveDate >= firstMonth &&
                                    entry.EffectiveDate < monthEnd)
                                .SumAsync(entry => (decimal?)entry.Units) ?? 0m;
                            var grant = LeaveAccrualCalculator
                                .CalculateCumulativeDelta(
                                    cumulative,
                                    previouslyCredited);
                            created += await AppendAccrualIfNewAsync(
                                employee.Id,
                                leaveType.Id,
                                policy,
                                monthStart,
                                monthEnd,
                                monthEnd.AddDays(1),
                                grant,
                                null,
                                nowUtc);
                        }
                    }
                }

                var payrollPolicies = policies.Where(policy =>
                    policy.IsAccrualEnabled &&
                    policy.AccrualFrequency ==
                        LeaveAccrualFrequency.PerPayPeriod &&
                    policy.LeavePayrollCalendar is not null);
                foreach (var policy in payrollPolicies)
                {
                    var calendar = policy.LeavePayrollCalendar!;
                    var calendarTimeZone = GetTimeZone(calendar.TimeZoneId);
                    var calendarDate = LeaveAccrualCalculator.GetBusinessDate(
                        nowUtc,
                        calendarTimeZone);
                    var periods = payPeriods
                        .Where(period =>
                            period.LeavePayrollCalendarId == calendar.Id &&
                            period.PostingDate <= calendarDate)
                        .OrderBy(period => period.PostingDate)
                        .ToList();
                    foreach (var period in periods)
                    {
                        var payPeriodDays = ResolveAccrualDays(
                            resolutionData,
                            leaveType.Id,
                            period.StartDate,
                            period.EndDate);
                        var group = payPeriodDays.SingleOrDefault(item =>
                            item.Policy.Id == policy.Id);
                        if (group is null)
                        {
                            continue;
                        }

                        var entitlement = RequireEntitlement(
                            policy,
                            leaveType.Id);
                        var raw = entitlement.AnnualEntitlement /
                            calendar.ExpectedPeriodsInEntitlementYear *
                            group.Dates.Count /
                            (period.EndDate.DayNumber -
                             period.StartDate.DayNumber + 1);
                        var cumulativeKey = new AccrualCycleKey(
                            employee.Id,
                            leaveType.Id,
                            policy.Id,
                            period.EntitlementYear);
                        payPeriodUnrounded.TryGetValue(
                            cumulativeKey,
                            out var cumulative);
                        cumulative += raw;
                        payPeriodUnrounded[cumulativeKey] = cumulative;

                        var key = LeaveAccrualCalculator
                            .CreateIdempotencyKey(
                                employee.Id,
                                leaveType.Id,
                                policy.Id,
                                policy.Version,
                                period.StartDate,
                                period.EndDate,
                                period.Id);
                        if (await HasLedgerKeyAsync(key))
                        {
                            continue;
                        }

                        var previouslyCredited = await context
                            .LeaveBalanceLedgerEntries
                            .Where(entry =>
                                entry.EmployeeUserId == employee.Id &&
                                entry.LeaveTypeId == leaveType.Id &&
                                entry.LeavePolicyId == policy.Id &&
                                entry.EntryType == LeaveLedgerEntryType.Accrual &&
                                entry.PayPeriod != null &&
                                entry.PayPeriod.EntitlementYear ==
                                    period.EntitlementYear &&
                                entry.PayPeriod.PeriodNumber <
                                    period.PeriodNumber)
                            .SumAsync(entry => (decimal?)entry.Units) ?? 0m;
                        var grant = LeaveAccrualCalculator
                            .CalculateCumulativeDelta(
                                cumulative,
                                previouslyCredited);
                        created += await AppendAccrualIfNewAsync(
                            employee.Id,
                            leaveType.Id,
                            policy,
                            period.StartDate,
                            period.EndDate,
                            period.PostingDate,
                            grant,
                            period.Id,
                            nowUtc);
                    }
                }
            }
        }

        return created;
    }

    private async Task<LeavePolicyResolutionData>
        GetAccrualResolutionDataAsync(
            User employee,
            IReadOnlyList<LeavePolicy> policies)
    {
        var profiles = await context.EmployeeEmploymentProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == employee.Id)
            .ToListAsync();
        var assignments = await context.LeavePolicyAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == employee.Id)
            .ToListAsync();
        return new LeavePolicyResolutionData(
            employee,
            profiles,
            assignments,
            policies,
            new Dictionary<int, IReadOnlySet<DayOfWeek>>(),
            new Dictionary<int, IReadOnlySet<DateOnly>>());
    }

    private static IReadOnlyList<AccrualDayGroup> ResolveAccrualDays(
        LeavePolicyResolutionData data,
        int leaveTypeId,
        DateOnly startDate,
        DateOnly endDate)
    {
        var grouped = new Dictionary<int, AccrualDayGroup>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var profiles = data.EmploymentProfiles
                .Where(profile =>
                    profile.UserId == data.Employee.Id &&
                    IsEffective(profile.EffectiveFrom, profile.EffectiveTo, date))
                .ToList();
            if (profiles.Count > 1)
            {
                throw new LeaveConfigurationException(
                    "Employee employment metadata has overlapping effective periods.");
            }

            var profile = profiles.SingleOrDefault();
            var assignments = data.Assignments
                .Where(assignment =>
                    assignment.UserId == data.Employee.Id &&
                    IsEffective(
                        assignment.EffectiveFrom,
                        assignment.EffectiveTo,
                        date))
                .ToList();
            var hasApplicablePolicy = HasApplicablePolicy(
                data,
                profile,
                date);
            if (!hasApplicablePolicy)
            {
                continue;
            }

            var resolution = LeavePolicyResolver.Resolve(
                data.Employee,
                data.EmploymentProfiles,
                data.Assignments,
                data.Policies,
                leaveTypeId,
                date);
            var eligibleFrom = LeaveAccrualCalculator
                .DetermineFirstEligibleDate(
                    profile?.HireDate,
                    resolution.Policy.EffectiveFrom,
                    resolution.Policy.ProbationPeriodDays,
                    resolution.Policy.AllowsLeaveDuringProbation);
            if (date < eligibleFrom ||
                !resolution.Policy.IsAccrualEnabled ||
                resolution.Policy.AccrualFrequency is null)
            {
                continue;
            }

            if (!grouped.TryGetValue(
                    resolution.Policy.Id,
                    out var group))
            {
                group = new AccrualDayGroup(resolution.Policy, []);
                grouped.Add(resolution.Policy.Id, group);
            }

            group.Dates.Add(date);
        }

        return grouped.Values.ToList();
    }

    private static bool HasApplicablePolicy(
        LeavePolicyResolutionData data,
        EmployeeEmploymentProfile? profile,
        DateOnly date)
    {
        if (data.Assignments.Any(assignment =>
                assignment.UserId == data.Employee.Id &&
                IsEffective(
                    assignment.EffectiveFrom,
                    assignment.EffectiveTo,
                    date)))
        {
            return true;
        }

        return data.Policies.Any(policy =>
            (policy.Status is LeavePolicyStatus.Published or
                LeavePolicyStatus.Superseded) &&
            IsEffective(
                policy.EffectiveFrom,
                policy.EffectiveTo,
                date) &&
            (policy.Status != LeavePolicyStatus.Superseded ||
             !data.Policies.Any(successor =>
                 successor.Id == policy.SupersededByPolicyId &&
                 successor.EffectiveFrom <= date)) &&
            (policy.Scope == LeavePolicyScope.Company ||
             (policy.Scope == LeavePolicyScope.Department &&
              policy.DepartmentId == data.Employee.DepartmentId) ||
             (policy.Scope == LeavePolicyScope.Designation &&
              policy.DesignationId == profile?.DesignationId) ||
             (policy.Scope == LeavePolicyScope.EmploymentType &&
              policy.EmploymentType == profile?.EmploymentType)));
    }

    private async Task<int> AppendAccrualIfNewAsync(
        int employeeUserId,
        int leaveTypeId,
        LeavePolicy policy,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly effectiveDate,
        decimal units,
        long? payPeriodId,
        DateTime nowUtc)
    {
        if (units <= 0)
        {
            return 0;
        }

        var idempotencyKey = LeaveAccrualCalculator.CreateIdempotencyKey(
            employeeUserId,
            leaveTypeId,
            policy.Id,
            policy.Version,
            periodStart,
            periodEnd,
            payPeriodId);
        var entry = new LeaveBalanceLedgerEntry(
            employeeUserId,
            leaveTypeId,
            policy.Id,
            null,
            payPeriodId,
            LeaveLedgerEntryType.Accrual,
            effectiveDate,
            LeaveAccrualCalculator.RoundUnits(units),
            idempotencyKey,
            nowUtc);
        return await TryAppendLedgerWriteAsync(
            new LeaveLedgerWrite(entry, []))
            ? 1
            : 0;
    }

    private Task<bool> HasLedgerKeyAsync(string idempotencyKey) =>
        context.LeaveBalanceLedgerEntries
            .AnyAsync(entry => entry.IdempotencyKey == idempotencyKey);

    private async Task<decimal> GetAllocatedUnitsAsync(
        long sourceEntryId,
        LeaveLedgerEntryType entryType,
        DateOnly? effectiveDate = null)
    {
        var query = context.LeaveBalanceAllocations
            .Where(allocation =>
                allocation.SourceEntryId == sourceEntryId &&
                allocation.LedgerEntry.EntryType == entryType);
        if (effectiveDate.HasValue)
        {
            query = query.Where(allocation =>
                allocation.LedgerEntry.EffectiveDate == effectiveDate.Value);
        }

        return await query.SumAsync(allocation =>
            (decimal?)allocation.Units) ?? 0m;
    }

    private async Task<bool> AppendExpiryAsync(
        int employeeUserId,
        int leaveTypeId,
        int? leavePolicyId,
        long sourceEntryId,
        DateOnly expiryDate,
        decimal units,
        DateTime nowUtc)
    {
        if (units <= 0)
        {
            return false;
        }

        var expiredThroughThisEntry = await GetAllocatedUnitsAsync(
            sourceEntryId,
            LeaveLedgerEntryType.Expiry);
        var totalExpired = expiredThroughThisEntry + units;
        var key = string.Join(
            ':',
            "leave-expiry",
            sourceEntryId,
            totalExpired.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture));
        var entry = new LeaveBalanceLedgerEntry(
            employeeUserId,
            leaveTypeId,
            leavePolicyId,
            null,
            null,
            LeaveLedgerEntryType.Expiry,
            expiryDate.AddDays(1),
            units,
            key,
            nowUtc,
            expiresOn: expiryDate,
            sourceEntryId: sourceEntryId);
        return await TryAppendLedgerWriteAsync(
            new LeaveLedgerWrite(
                entry,
                [
                    new LeaveLedgerAllocationInput(
                        sourceEntryId,
                        expiryDate,
                        units)
                ]));
    }

    private static LeavePolicyEntitlement RequireEntitlement(
        LeavePolicy policy,
        int leaveTypeId) =>
        policy.Entitlements.SingleOrDefault(
            entitlement => entitlement.LeaveTypeId == leaveTypeId)
        ?? throw new LeaveConfigurationException(
            "An accruing leave policy is missing its leave type entitlement.");

    private static bool IsEffective(
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateOnly date) =>
        effectiveFrom <= date &&
        (!effectiveTo.HasValue || effectiveTo.Value >= date);

    private static void EnsureUtc(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The processing timestamp must be UTC.",
                nameof(timestampUtc));
        }
    }

    private static TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new LeaveConfigurationException(
                "A configured leave business time zone is required.");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new LeaveConfigurationException(
                "The configured leave business time zone is not recognized.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new LeaveConfigurationException(
                "The configured leave business time zone is invalid.");
        }
    }

    private sealed record AccrualCycleKey(
        int EmployeeUserId,
        int LeaveTypeId,
        int PolicyId,
        int EntitlementYear);

    private sealed record AccrualDayGroup(
        LeavePolicy Policy,
        List<DateOnly> Dates);

    private async Task ValidateAllocationsAsync(LeaveLedgerWrite write)
    {
        if (write.Allocations.Count == 0)
        {
            if (write.Entry.EntryType is LeaveLedgerEntryType.Reservation or
                    LeaveLedgerEntryType.Used or
                    LeaveLedgerEntryType.ScheduledCommitment or
                    LeaveLedgerEntryType.Expiry)
            {
                throw new InvalidOperationException(
                    "This ledger operation requires source allocations.");
            }

            return;
        }

        if (write.Allocations.Any(x =>
                x.Units <= 0 ||
                x.Units != decimal.Round(
                    x.Units,
                    2,
                    MidpointRounding.AwayFromZero)))
        {
            throw new InvalidOperationException(
                "Ledger allocation units are invalid.");
        }

        var sources = await GetAvailableSourcesAsync(
            write.Entry.EmployeeUserId,
            write.Entry.LeaveTypeId,
            write.Entry.EffectiveDate);
        var sourceMap = sources.ToDictionary(x => x.SourceId);
        foreach (var group in write.Allocations.GroupBy(
                     x => x.SourceEntryId))
        {
            if (!sourceMap.TryGetValue(group.Key, out var source) ||
                group.Any(allocation =>
                    source.ExpiresOn.HasValue &&
                    source.ExpiresOn.Value < allocation.LeaveDate) ||
                group.Sum(allocation => allocation.Units) >
                    source.GrantedUnits -
                    source.ConsumedUnits -
                    source.ReservedUnits)
            {
                throw new InvalidOperationException(
                    "The ledger write allocates unavailable or expired leave units.");
            }
        }
    }

    private static decimal Sum(
        IEnumerable<LeaveBalanceLedgerEntry> entries,
        LeaveLedgerEntryType type) =>
        entries.Where(entry => entry.EntryType == type)
            .Sum(entry => entry.Units);
}
