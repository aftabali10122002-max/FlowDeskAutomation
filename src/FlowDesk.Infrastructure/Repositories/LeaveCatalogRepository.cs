using System.Data;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Repositories;

public sealed class LeaveCatalogRepository(
    AppDbContext context) : ILeaveCatalogRepository
{
    public async Task<IReadOnlyList<LeaveWorkSchedule>> GetSchedulesAsync() =>
        await context.LeaveWorkSchedules
            .AsNoTracking()
            .Include(schedule => schedule.Days)
            .OrderBy(schedule => schedule.Name)
            .ToListAsync();

    public async Task<LeaveWorkSchedule> CreateScheduleAsync(
        LeaveWorkSchedule schedule,
        IReadOnlyList<LeaveWorkScheduleDay> days)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (await context.LeaveWorkSchedules.AnyAsync(
                existing => existing.Code == schedule.Code))
        {
            throw new LeaveInvalidInputException(
                "A work schedule with this code already exists.");
        }

        if (days.Count != 7 ||
            days.Select(day => day.DayOfWeek).Distinct().Count() != 7)
        {
            throw new LeaveInvalidInputException(
                "A work schedule must define each weekday exactly once.");
        }

        context.LeaveWorkSchedules.Add(schedule);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return schedule;
    }

    public async Task<IReadOnlyList<LeaveHolidayCalendar>>
        GetHolidayCalendarsAsync() =>
        await context.LeaveHolidayCalendars
            .AsNoTracking()
            .Include(calendar => calendar.Holidays)
            .OrderBy(calendar => calendar.Name)
            .ToListAsync();

    public async Task<LeaveHolidayCalendar> CreateHolidayCalendarAsync(
        LeaveHolidayCalendar calendar)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (await context.LeaveHolidayCalendars.AnyAsync(
                existing => existing.Code == calendar.Code))
        {
            throw new LeaveInvalidInputException(
                "A holiday calendar with this code already exists.");
        }

        context.LeaveHolidayCalendars.Add(calendar);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return calendar;
    }

    public async Task<LeaveHoliday> AddHolidayAsync(LeaveHoliday holiday)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await context.LeaveHolidayCalendars.AnyAsync(
                calendar => calendar.Id == holiday.LeaveHolidayCalendarId))
        {
            throw new LeaveNotFoundException("Holiday calendar");
        }

        if (await context.LeaveHolidays.AnyAsync(existing =>
                existing.LeaveHolidayCalendarId ==
                    holiday.LeaveHolidayCalendarId &&
                existing.Date == holiday.Date))
        {
            throw new LeaveInvalidInputException(
                "A holiday is already configured for this date.");
        }

        context.LeaveHolidays.Add(holiday);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return holiday;
    }

    public async Task<IReadOnlyList<LeavePayrollCalendar>>
        GetPayrollCalendarsAsync() =>
        await context.LeavePayrollCalendars
            .AsNoTracking()
            .Include(calendar => calendar.Periods)
            .OrderBy(calendar => calendar.Name)
            .ToListAsync();

    public async Task<LeavePayrollCalendar> CreatePayrollCalendarAsync(
        LeavePayrollCalendar calendar)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (await context.LeavePayrollCalendars.AnyAsync(
                existing => existing.Code == calendar.Code))
        {
            throw new LeaveInvalidInputException(
                "A payroll calendar with this code already exists.");
        }

        context.LeavePayrollCalendars.Add(calendar);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return calendar;
    }

    public async Task<LeavePayPeriod> AddPayPeriodAsync(
        LeavePayPeriod period)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await context.LeavePayrollCalendars.AnyAsync(calendar =>
                calendar.Id == period.LeavePayrollCalendarId &&
                calendar.IsEnabled))
        {
            throw new LeaveNotFoundException("Enabled payroll calendar");
        }

        var overlaps = await context.LeavePayPeriods.AnyAsync(existing =>
            existing.LeavePayrollCalendarId ==
                period.LeavePayrollCalendarId &&
            existing.StartDate <= period.EndDate &&
            period.StartDate <= existing.EndDate);
        if (overlaps)
        {
            throw new LeaveInvalidInputException(
                "Payroll periods in the same calendar cannot overlap.");
        }

        context.LeavePayPeriods.Add(period);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return period;
    }

    public async Task<IReadOnlyList<Designation>> GetDesignationsAsync() =>
        await context.Designations
            .AsNoTracking()
            .Where(designation => designation.IsEnabled)
            .OrderBy(designation => designation.Name)
            .ToListAsync();

    public async Task<Designation> CreateDesignationAsync(
        Designation designation)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable);
        if (await context.Designations.AnyAsync(existing =>
                existing.Code == designation.Code ||
                existing.Name == designation.Name))
        {
            throw new LeaveInvalidInputException(
                "A designation with this code or name already exists.");
        }

        context.Designations.Add(designation);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return designation;
    }
}
