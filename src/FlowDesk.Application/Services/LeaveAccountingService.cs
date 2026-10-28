using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed class LeaveAccountingService(
    ILeaveAccountingRepository accountingRepository,
    IUserRepository userRepository)
{
    public async Task<IReadOnlyList<LeaveType>> GetTypesAsync(int actorUserId)
    {
        await RequireActiveUserAsync(actorUserId);
        return await accountingRepository.GetLeaveTypesAsync(enabledOnly: true);
    }

    public async Task<IReadOnlyList<LeaveType>> GetAllTypesAsync(
        int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await accountingRepository.GetLeaveTypesAsync(enabledOnly: false);
    }

    public async Task<LeaveType> CreateTypeAsync(
        int adminUserId,
        LeaveTypeInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await accountingRepository.CreateLeaveTypeAsync(
                new LeaveType(input.Code, input.DisplayName, input.IsPaid));
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<LeaveType> UpdateTypeAsync(
        int adminUserId,
        int leaveTypeId,
        LeaveTypeUpdateInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        if (leaveTypeId <= 0)
        {
            throw new LeaveInvalidInputException(
                "A valid leave type is required.");
        }

        try
        {
            return await accountingRepository.UpdateLeaveTypeAsync(
                leaveTypeId,
                input.DisplayName,
                input.IsPaid,
                input.IsEnabled);
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<IReadOnlyList<LeaveBalanceSummary>> GetBalancesAsync(
        int actorUserId,
        int employeeUserId,
        DateOnly asOfDate)
    {
        var actor = await RequireActiveUserAsync(actorUserId);
        var employee = await userRepository.GetByIdAsync(employeeUserId);
        if (employee is null)
        {
            throw new LeaveNotFoundException("Employee");
        }

        EnsureCanViewEmployee(actor, employee);
        return await accountingRepository.GetBalancesAsync(
            employeeUserId,
            asOfDate);
    }

    public async Task<IReadOnlyList<LeaveLedgerHistoryItem>> GetHistoryAsync(
        int actorUserId,
        int employeeUserId,
        int? leaveTypeId,
        DateOnly? throughDate)
    {
        var actor = await RequireActiveUserAsync(actorUserId);
        var employee = await userRepository.GetByIdAsync(employeeUserId);
        if (employee is null)
        {
            throw new LeaveNotFoundException("Employee");
        }

        EnsureCanViewEmployee(actor, employee);
        if (leaveTypeId is <= 0)
        {
            throw new LeaveInvalidInputException(
                "The leave type filter is invalid.");
        }

        var history = await accountingRepository.GetLedgerHistoryAsync(
            employeeUserId,
            leaveTypeId,
            throughDate);
        return actor.Role == UserRoles.Manager && actor.Id != employee.Id
            ? history.Select(entry => entry with { Reason = null }).ToList()
            : history;
    }

    public async Task<IReadOnlyList<LeaveTeamBalanceSummary>>
        GetTeamBalancesAsync(int managerUserId, DateOnly asOfDate)
    {
        var manager = await RequireActiveUserAsync(managerUserId);
        if (manager.Role != UserRoles.Manager ||
            manager.DepartmentId is not int departmentId)
        {
            throw new LeaveUnauthorizedException();
        }

        var employees = await userRepository.GetActiveByDepartmentAsync(
            departmentId);
        var team = employees
            .Where(employee =>
                employee.Role == UserRoles.Employee)
            .ToList();
        var balances = await accountingRepository
            .GetBalancesForEmployeesAsync(
                team.Select(employee => employee.Id).ToList(),
                asOfDate);
        return team.Select(employee => new LeaveTeamBalanceSummary(
            employee.Id,
            employee.FullName,
            balances.GetValueOrDefault(employee.Id) ?? []))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<
        int,
        IReadOnlyList<LeaveBalanceSummary>>> GetBalancesForReportAsync(
            int actorUserId,
            IReadOnlyList<int> employeeUserIds,
            DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(employeeUserIds);
        var actor = await RequireActiveUserAsync(actorUserId);
        var employeeIds = employeeUserIds.Distinct().ToArray();
        if (employeeIds.Any(id => id <= 0))
        {
            throw new LeaveInvalidInputException(
                "The report employee filter is invalid.");
        }

        if (actor.Role == UserRoles.Employee &&
            employeeIds.Any(id => id != actor.Id))
        {
            throw new LeaveUnauthorizedException();
        }

        if (actor.Role == UserRoles.Manager)
        {
            if (actor.DepartmentId is not int departmentId)
            {
                throw new LeaveUnauthorizedException();
            }

            var authorizedEmployeeIds = (await userRepository
                    .GetActiveByDepartmentAsync(departmentId))
                .Where(user => user.Role == UserRoles.Employee)
                .Select(user => user.Id)
                .ToHashSet();
            if (employeeIds.Any(id => !authorizedEmployeeIds.Contains(id)))
            {
                throw new LeaveUnauthorizedException();
            }
        }
        else if (actor.Role == UserRoles.Admin)
        {
            foreach (var employeeUserId in employeeIds)
            {
                var employee = await userRepository.GetByIdAsync(employeeUserId);
                if (employee is null || employee.Role != UserRoles.Employee)
                {
                    throw new LeaveNotFoundException("Employee");
                }
            }
        }
        else if (actor.Role != UserRoles.Employee)
        {
            throw new LeaveUnauthorizedException();
        }

        return employeeIds.Length == 0
            ? new Dictionary<int, IReadOnlyList<LeaveBalanceSummary>>()
            : await accountingRepository.GetBalancesForEmployeesAsync(
                employeeIds,
                asOfDate);
    }

    public async Task<bool> AdjustBalanceAsync(
        int adminUserId,
        LeaveManualAdjustmentInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        if (input.EmployeeUserId <= 0 ||
            input.LeaveTypeId <= 0 ||
            input.Units == 0 ||
            input.Units < -9_999_999.99m ||
            input.Units > 9_999_999.99m ||
            input.Units != LeaveAccrualCalculator.RoundUnits(input.Units) ||
            string.IsNullOrWhiteSpace(input.Reason) ||
            input.Reason.Trim().Length > 1000 ||
            string.IsNullOrWhiteSpace(input.IdempotencyKey) ||
            input.IdempotencyKey.Trim().Length > 200)
        {
            throw new LeaveInvalidInputException(
                "The leave adjustment values are invalid.");
        }

        var employee = await userRepository.GetByIdAsync(input.EmployeeUserId);
        if (employee is null)
        {
            throw new LeaveNotFoundException("Employee");
        }

        if (!(await accountingRepository.GetEnabledLeaveTypesAsync())
                .Any(type => type.Id == input.LeaveTypeId))
        {
            throw new LeaveNotFoundException("Enabled leave type");
        }

        var allocations = new List<LeaveLedgerAllocationInput>();
        if (input.Units < 0)
        {
            var sources = await accountingRepository.GetAvailableSourcesAsync(
                input.EmployeeUserId,
                input.LeaveTypeId,
                input.EffectiveDate);
            var plan = LeaveAllocationPlanner.PlanReservation(
                [
                    new RequestedLeaveDate(
                        input.EffectiveDate,
                        Math.Abs(input.Units),
                        AllowsNegativeBalance: true)
                ],
                sources.Select(source => new LeaveBalanceSource(
                    source.SourceId,
                    source.Kind,
                    source.GrantedUnits,
                    source.ConsumedUnits,
                    source.ReservedUnits,
                    source.EarnedOn,
                    source.ExpiresOn)),
                input.EffectiveDate);
            if (plan.UnfundedUnits > 0)
            {
                throw new LeaveInvalidInputException(
                    "A negative adjustment cannot exceed uncommitted leave sources.");
            }

            allocations.AddRange(plan.Allocations.Select(allocation =>
                new LeaveLedgerAllocationInput(
                    allocation.SourceId,
                    allocation.LeaveDate,
                    allocation.Units)));
        }

        var entry = new LeaveBalanceLedgerEntry(
            input.EmployeeUserId,
            input.LeaveTypeId,
            null,
            null,
            null,
            LeaveLedgerEntryType.Adjustment,
            input.EffectiveDate,
            input.Units,
            input.IdempotencyKey,
            DateTime.UtcNow,
            input.Reason,
            adminUserId);
        return await accountingRepository.TryAppendLedgerWriteAsync(
            new LeaveLedgerWrite(entry, allocations));
    }

    private async Task<User> RequireActiveUserAsync(int userId)
    {
        var user = userId > 0
            ? await userRepository.GetByIdAsync(userId)
            : null;
        return user is { IsActive: true }
            ? user
            : throw new LeaveUnauthorizedException();
    }

    private async Task RequireAdminAsync(int adminUserId)
    {
        var admin = await RequireActiveUserAsync(adminUserId);
        if (admin.Role != UserRoles.Admin)
        {
            throw new LeaveUnauthorizedException();
        }
    }

    private static void EnsureCanViewEmployee(User actor, User employee)
    {
        if (actor.Role == UserRoles.Admin ||
            actor.Id == employee.Id ||
            (actor.Role == UserRoles.Manager &&
             employee.IsActive &&
             employee.Role == UserRoles.Employee &&
             actor.DepartmentId.HasValue &&
             actor.DepartmentId == employee.DepartmentId))
        {
            return;
        }

        throw new LeaveUnauthorizedException();
    }
}
