using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class LeaveCatalogService(
    ILeaveCatalogRepository catalogRepository,
    IUserRepository userRepository)
{
    public async Task<IReadOnlyList<LeaveWorkSchedule>> GetSchedulesAsync(
        int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await catalogRepository.GetSchedulesAsync();
    }

    public async Task<LeaveWorkSchedule> CreateScheduleAsync(
        int adminUserId,
        LeaveScheduleInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Days is null ||
            input.Days.Count != 7 ||
            input.Days.Select(day => day.DayOfWeek).Distinct().Count() != 7)
        {
            throw new LeaveInvalidInputException(
                "A work schedule must define each weekday exactly once.");
        }

        try
        {
            var schedule = new LeaveWorkSchedule(input.Code, input.Name);
            foreach (var day in input.Days)
            {
                schedule.AddDay(
                    day.DayOfWeek,
                    day.IsWorkingDay,
                    day.StandardHours);
            }

            return await catalogRepository.CreateScheduleAsync(
                schedule,
                schedule.Days.ToList());
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<IReadOnlyList<LeaveHolidayCalendar>>
        GetHolidayCalendarsAsync(int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await catalogRepository.GetHolidayCalendarsAsync();
    }

    public async Task<LeaveHolidayCalendar> CreateHolidayCalendarAsync(
        int adminUserId,
        LeaveHolidayCalendarInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await catalogRepository.CreateHolidayCalendarAsync(
                new LeaveHolidayCalendar(
                    input.Code,
                    input.Name,
                    input.Region));
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<LeaveHoliday> AddHolidayAsync(
        int adminUserId,
        int calendarId,
        LeaveHolidayInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await catalogRepository.AddHolidayAsync(
                new LeaveHoliday(calendarId, input.Date, input.Name));
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<IReadOnlyList<LeavePayrollCalendar>>
        GetPayrollCalendarsAsync(int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await catalogRepository.GetPayrollCalendarsAsync();
    }

    public async Task<LeavePayrollCalendar> CreatePayrollCalendarAsync(
        int adminUserId,
        LeavePayrollCalendarInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                input.TimeZoneId);
            return await catalogRepository.CreatePayrollCalendarAsync(
                new LeavePayrollCalendar(
                    input.Code,
                    input.Name,
                    timeZone.Id,
                    input.ExpectedPeriodsInEntitlementYear));
        }
        catch (TimeZoneNotFoundException)
        {
            throw new LeaveInvalidInputException(
                "The payroll calendar time zone is not recognized.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new LeaveInvalidInputException(
                "The payroll calendar time zone is invalid.");
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<LeavePayPeriod> AddPayPeriodAsync(
        int adminUserId,
        int calendarId,
        LeavePayPeriodInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await catalogRepository.AddPayPeriodAsync(
                new LeavePayPeriod(
                    calendarId,
                    input.StartDate,
                    input.EndDate,
                    input.PostingDate,
                    input.EntitlementYear,
                    input.PeriodNumber));
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    public async Task<IReadOnlyList<Designation>> GetDesignationsAsync(
        int adminUserId)
    {
        await RequireAdminAsync(adminUserId);
        return await catalogRepository.GetDesignationsAsync();
    }

    public async Task<Designation> CreateDesignationAsync(
        int adminUserId,
        DesignationInput input)
    {
        await RequireAdminAsync(adminUserId);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await catalogRepository.CreateDesignationAsync(
                new Designation(input.Code, input.Name));
        }
        catch (ArgumentException exception)
        {
            throw new LeaveInvalidInputException(exception.Message);
        }
    }

    private async Task RequireAdminAsync(int adminUserId)
    {
        var admin = adminUserId > 0
            ? await userRepository.GetByIdAsync(adminUserId)
            : null;
        if (admin is not { IsActive: true } ||
            admin.Role != UserRoles.Admin)
        {
            throw new LeaveUnauthorizedException();
        }
    }
}
