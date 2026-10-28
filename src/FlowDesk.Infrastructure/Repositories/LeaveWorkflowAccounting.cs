using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

internal sealed class LeaveWorkflowAccounting(AppDbContext context)
{
    public async Task PersistRequestAsync(
        ApprovalRequest request,
        DateTime nowUtc)
    {
        var details = request.LeaveDetails;
        if (details is null)
        {
            return;
        }

        if (details.CancellationOfRequestId.HasValue)
        {
            return;
        }

        if (request.CreatedByUserId is not int employeeUserId ||
            details.ConfiguredLeaveTypeId is not int leaveTypeId ||
            details.CalculatedLeaveUnits is not decimal totalUnits ||
            details.UnitBreakdown.Count == 0)
        {
            throw new InvalidOperationException(
                "Leave request policy calculation was not completed.");
        }

        var policyDays = details.UnitBreakdown
            .Select(item => new LeaveRequestPolicyDay(
                request.Id,
                item.Date,
                item.PolicyId,
                item.Units,
                item.AllowsNegativeBalance))
            .ToList();
        context.LeaveRequestPolicyDays.AddRange(policyDays);
        await context.SaveChangesAsync();

        var policyIds = policyDays
            .Select(day => day.LeavePolicyId)
            .Distinct()
            .ToArray();
        var timeZoneIds = await context.LeavePolicies
            .Where(policy => policyIds.Contains(policy.Id))
            .Select(policy => new
            {
                policy.Id,
                TimeZoneId = policy.LeavePayrollCalendar == null
                    ? null
                    : policy.LeavePayrollCalendar.TimeZoneId
            })
            .ToDictionaryAsync(item => item.Id, item => item.TimeZoneId);

        var allocations = new List<LeaveSourceAllocation>();
        var alreadyAllocated = new Dictionary<long, decimal>();
        var sourcesByBusinessDate =
            new Dictionary<DateOnly, IReadOnlyList<LeaveBalanceSource>>();
        DateOnly? reservationBusinessDate = null;

        foreach (var day in policyDays.OrderBy(day => day.LeaveDate))
        {
            var timeZoneId = timeZoneIds[day.LeavePolicyId];
            var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var businessDate = LeaveAllocationPlanner.GetBusinessDate(
                nowUtc,
                timeZone);
            reservationBusinessDate ??= businessDate;
            if (!sourcesByBusinessDate.TryGetValue(
                    businessDate,
                    out var currentSources))
            {
                currentSources = await GetAvailableSourcesAsync(
                    employeeUserId,
                    leaveTypeId,
                    businessDate);
                sourcesByBusinessDate.Add(businessDate, currentSources);
            }

            var adjustedSources = currentSources
                .Select(source => source with
                {
                    ConsumedUnits = source.ConsumedUnits +
                        alreadyAllocated.GetValueOrDefault(source.SourceId)
                })
                .ToList();
            var plan = LeaveAllocationPlanner.PlanReservation(
                [new RequestedLeaveDate(day.LeaveDate, day.Units)],
                adjustedSources,
                businessDate,
                day.AllowsNegativeBalance);

            foreach (var allocation in plan.Allocations)
            {
                allocations.Add(allocation);
                alreadyAllocated[allocation.SourceId] =
                    alreadyAllocated.GetValueOrDefault(
                        allocation.SourceId) + allocation.Units;
            }
        }

        var effectiveDate = reservationBusinessDate
            ?? DateOnly.FromDateTime(nowUtc);
        var reservation = new LeaveBalanceLedgerEntry(
            employeeUserId,
            leaveTypeId,
            policyDays[0].LeavePolicyId,
            request.Id,
            null,
            LeaveLedgerEntryType.Reservation,
            effectiveDate,
            totalUnits,
            $"leave-request:{request.Id}:reservation",
            nowUtc);
        context.LeaveBalanceLedgerEntries.Add(reservation);
        await context.SaveChangesAsync();
        AddAllocations(reservation.Id, allocations);
        await context.SaveChangesAsync();
    }

    public async Task CompleteRequestAsync(
        int requestId,
        bool approved,
        DateTime nowUtc,
        int actorUserId)
    {
        var cancellationOfRequestId = await context
            .Set<LeaveRequestDetails>()
            .Where(details => details.RequestId == requestId)
            .Select(details => details.CancellationOfRequestId)
            .SingleOrDefaultAsync();
        if (cancellationOfRequestId.HasValue)
        {
            if (approved)
            {
                await CompleteCancellationRequestAsync(
                    requestId,
                    cancellationOfRequestId.Value,
                    nowUtc,
                    actorUserId);
            }

            return;
        }

        var policyDays = await context.LeaveRequestPolicyDays
            .Where(day => day.RequestId == requestId)
            .OrderBy(day => day.LeaveDate)
            .ToListAsync();
        if (policyDays.Count == 0)
        {
            return;
        }

        var reservation = await context.LeaveBalanceLedgerEntries
            .SingleOrDefaultAsync(entry =>
                entry.RequestId == requestId &&
                entry.EntryType == LeaveLedgerEntryType.Reservation);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                "The leave reservation ledger entry is missing.");
        }

        var businessDate = DateOnly.FromDateTime(nowUtc);
        await ReleaseReservationAsync(
            requestId,
            businessDate,
            actorUserId);
        if (!approved)
        {
            return;
        }

        var reservationAllocations = await context.LeaveBalanceAllocations
            .Where(allocation =>
                allocation.LedgerEntryId == reservation.Id)
            .ToListAsync();
        foreach (var day in policyDays)
        {
            var entryType = day.LeaveDate <= businessDate
                ? LeaveLedgerEntryType.Used
                : LeaveLedgerEntryType.ScheduledCommitment;
            var effectiveDate = entryType == LeaveLedgerEntryType.Used
                ? day.LeaveDate
                : businessDate;
            var idempotencyKey =
                $"leave-request:{requestId}:{entryType}:{day.LeaveDate:yyyyMMdd}";
            if (await context.LeaveBalanceLedgerEntries.AnyAsync(
                    entry => entry.IdempotencyKey == idempotencyKey))
            {
                continue;
            }

            var entry = new LeaveBalanceLedgerEntry(
                reservation.EmployeeUserId,
                reservation.LeaveTypeId,
                day.LeavePolicyId,
                requestId,
                null,
                entryType,
                effectiveDate,
                day.Units,
                idempotencyKey,
                nowUtc,
                actorUserId: actorUserId,
                leaveDate: day.LeaveDate);
            context.LeaveBalanceLedgerEntries.Add(entry);
            await context.SaveChangesAsync();
            AddAllocations(
                entry.Id,
                reservationAllocations
                    .Where(allocation =>
                        allocation.LeaveDate == day.LeaveDate)
                    .Select(allocation => new LeaveSourceAllocation(
                        day.LeaveDate,
                        allocation.SourceEntryId,
                        allocation.Units)));
            await context.SaveChangesAsync();
        }
    }

    private async Task CompleteCancellationRequestAsync(
        int cancellationRequestId,
        int originalRequestId,
        DateTime nowUtc,
        int actorUserId)
    {
        var original = await context.ApprovalRequests
            .Include(request => request.LeaveDetails)
            .SingleOrDefaultAsync(request =>
                request.Id == originalRequestId &&
                request.Status == RequestStatus.Approved);
        if (original?.LeaveDetails is not { } details)
        {
            throw new InvalidOperationException(
                "The approved leave request for cancellation was not found.");
        }

        var businessDate = DateOnly.FromDateTime(nowUtc);
        if (details.StartDate <= businessDate)
        {
            throw new InvalidOperationException(
                "Approved leave cancellation is not supported after leave has started.");
        }

        var commitments = await context.LeaveBalanceLedgerEntries
            .Where(entry =>
                entry.RequestId == originalRequestId &&
                entry.EntryType == LeaveLedgerEntryType.ScheduledCommitment)
            .ToListAsync();
        if (commitments.Count == 0)
        {
            throw new InvalidOperationException(
                "The scheduled leave commitments required for cancellation are missing.");
        }

        foreach (var commitment in commitments)
        {
            var key =
                $"leave-cancellation:{cancellationRequestId}:scheduled-release:{commitment.Id}";
            if (await context.LeaveBalanceLedgerEntries.AnyAsync(
                    entry => entry.IdempotencyKey == key))
            {
                continue;
            }

            var release = new LeaveBalanceLedgerEntry(
                commitment.EmployeeUserId,
                commitment.LeaveTypeId,
                commitment.LeavePolicyId,
                cancellationRequestId,
                null,
                LeaveLedgerEntryType.ScheduledRelease,
                businessDate,
                commitment.Units,
                key,
                nowUtc,
                actorUserId: actorUserId,
                leaveDate: commitment.LeaveDate);
            context.LeaveBalanceLedgerEntries.Add(release);
            await context.SaveChangesAsync();
            var sourceAllocations = await context.LeaveBalanceAllocations
                .Where(allocation =>
                    allocation.LedgerEntryId == commitment.Id)
                .ToListAsync();
            AddAllocations(
                release.Id,
                sourceAllocations.Select(allocation =>
                    new LeaveSourceAllocation(
                        allocation.LeaveDate,
                        allocation.SourceEntryId,
                        allocation.Units)));
            await context.SaveChangesAsync();
        }

        var changed = await context.ApprovalRequests
            .Where(request =>
                request.Id == originalRequestId &&
                request.Status == RequestStatus.Approved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    request => request.Status,
                    RequestStatus.Cancelled));
        if (changed != 1)
        {
            throw new InvalidOperationException(
                "The original leave request changed while cancellation was being approved.");
        }
    }

    public async Task ReleaseReservationAsync(
        int requestId,
        DateOnly effectiveDate,
        int? actorUserId = null) =>
        await ReleaseReservationCoreAsync(
            requestId,
            effectiveDate,
            actorUserId);

    private async Task ReleaseReservationCoreAsync(
        int requestId,
        DateOnly effectiveDate,
        int? actorUserId)
    {
        var reservation = await context.LeaveBalanceLedgerEntries
            .SingleOrDefaultAsync(entry =>
                entry.RequestId == requestId &&
                entry.EntryType == LeaveLedgerEntryType.Reservation);
        if (reservation is null)
        {
            return;
        }

        var idempotencyKey =
            $"leave-request:{requestId}:reservation-release";
        if (await context.LeaveBalanceLedgerEntries.AnyAsync(
                entry => entry.IdempotencyKey == idempotencyKey))
        {
            return;
        }

        var reservationAllocations = await context.LeaveBalanceAllocations
            .Where(allocation =>
                allocation.LedgerEntryId == reservation.Id)
            .ToListAsync();
        var release = new LeaveBalanceLedgerEntry(
            reservation.EmployeeUserId,
            reservation.LeaveTypeId,
            reservation.LeavePolicyId,
            requestId,
            null,
            LeaveLedgerEntryType.ReservationRelease,
            effectiveDate,
            reservation.Units,
            idempotencyKey,
            DateTime.UtcNow,
            actorUserId: actorUserId);
        context.LeaveBalanceLedgerEntries.Add(release);
        await context.SaveChangesAsync();
        AddAllocations(
            release.Id,
            reservationAllocations
                .Select(allocation => new LeaveSourceAllocation(
                    allocation.LeaveDate,
                    allocation.SourceEntryId,
                    allocation.Units)));
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<LeaveBalanceSource>> GetAvailableSourcesAsync(
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

        var sourceIds = credits.Select(entry => entry.Id).ToArray();
        var allocations = await context.LeaveBalanceAllocations
            .AsNoTracking()
            .Where(allocation => sourceIds.Contains(allocation.SourceEntryId))
            .Select(allocation => new
            {
                allocation.SourceEntryId,
                allocation.Units,
                allocation.LeaveDate,
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
                        LeaveLedgerEntryType.Expiry ||
                    (allocation.EntryType == LeaveLedgerEntryType.Adjustment &&
                     allocation.EntryUnits < 0))
                .Sum(allocation => allocation.Units);
            var released = sourceAllocations
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

            return new LeaveBalanceSource(
                source.Id,
                source.EntryType == LeaveLedgerEntryType.CarryForward
                    ? LeaveBalanceSourceKind.CarryForward
                    : LeaveBalanceSourceKind.CurrentCycleEarned,
                source.Units,
                Math.Max(consumed - released, 0m),
                Math.Max(pending, 0m),
                source.EffectiveDate,
                source.ExpiresOn);
        }).ToList();
    }

    private void AddAllocations(
        long ledgerEntryId,
        IEnumerable<LeaveSourceAllocation> allocations)
    {
        foreach (var allocation in allocations)
        {
            context.LeaveBalanceAllocations.Add(new LeaveBalanceAllocation(
                ledgerEntryId,
                allocation.SourceId,
                allocation.LeaveDate,
                allocation.Units));
        }
    }
}
