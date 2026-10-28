using System.Security.Claims;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Errors;
using FlowDesk.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowDisk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/leave")]
public sealed class AdminLeaveCatalogController(
    LeaveCatalogService catalogService) : ControllerBase
{
    [HttpGet("work-schedules")]
    public async Task<IActionResult> GetWorkSchedules() =>
        Ok((await catalogService.GetSchedulesAsync(GetAdminUserId()))
            .Select(ToSchedule));

    [HttpPost("work-schedules")]
    public async Task<IActionResult> CreateWorkSchedule(
        [FromBody] LeaveScheduleInput input) =>
        Ok(ToSchedule(await catalogService.CreateScheduleAsync(
            GetAdminUserId(),
            input)));

    [HttpGet("holiday-calendars")]
    public async Task<IActionResult> GetHolidayCalendars() =>
        Ok((await catalogService.GetHolidayCalendarsAsync(GetAdminUserId()))
            .Select(ToHolidayCalendar));

    [HttpPost("holiday-calendars")]
    public async Task<IActionResult> CreateHolidayCalendar(
        [FromBody] LeaveHolidayCalendarInput input) =>
        Ok(ToHolidayCalendar(await catalogService.CreateHolidayCalendarAsync(
            GetAdminUserId(),
            input)));

    [HttpPost("holiday-calendars/{calendarId:int}/holidays")]
    public async Task<IActionResult> AddHoliday(
        int calendarId,
        [FromBody] LeaveHolidayInput input)
    {
        var holiday = await catalogService.AddHolidayAsync(
            GetAdminUserId(),
            calendarId,
            input);
        return Ok(new
        {
            holiday.Id,
            holiday.LeaveHolidayCalendarId,
            holiday.Date,
            holiday.Name
        });
    }

    [HttpGet("payroll-calendars")]
    public async Task<IActionResult> GetPayrollCalendars() =>
        Ok((await catalogService.GetPayrollCalendarsAsync(GetAdminUserId()))
            .Select(ToPayrollCalendar));

    [HttpPost("payroll-calendars")]
    public async Task<IActionResult> CreatePayrollCalendar(
        [FromBody] LeavePayrollCalendarInput input) =>
        Ok(ToPayrollCalendar(await catalogService.CreatePayrollCalendarAsync(
            GetAdminUserId(),
            input)));

    [HttpPost("payroll-calendars/{calendarId:int}/periods")]
    public async Task<IActionResult> AddPayPeriod(
        int calendarId,
        [FromBody] LeavePayPeriodInput input)
    {
        var period = await catalogService.AddPayPeriodAsync(
            GetAdminUserId(),
            calendarId,
            input);
        return Ok(new
        {
            period.Id,
            period.LeavePayrollCalendarId,
            period.StartDate,
            period.EndDate,
            period.PostingDate,
            period.EntitlementYear,
            period.PeriodNumber
        });
    }

    [HttpGet("designations")]
    public async Task<IActionResult> GetDesignations() =>
        Ok(await catalogService.GetDesignationsAsync(GetAdminUserId()));

    [HttpPost("designations")]
    public async Task<IActionResult> CreateDesignation(
        [FromBody] DesignationInput input)
    {
        var designation = await catalogService.CreateDesignationAsync(
            GetAdminUserId(),
            input);
        return CreatedAtAction(
            nameof(GetDesignations),
            new { },
            designation);
    }

    private int GetAdminUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
        {
            throw new LeaveUnauthorizedException();
        }

        return userId;
    }

    private static object ToSchedule(
        FlowDesk.Domain.Entities.LeaveWorkSchedule schedule) =>
        new
        {
            schedule.Id,
            schedule.Code,
            schedule.Name,
            schedule.IsEnabled,
            Days = schedule.Days.Select(day => new
            {
                day.DayOfWeek,
                day.IsWorkingDay,
                day.StandardHours
            })
        };

    private static object ToHolidayCalendar(
        FlowDesk.Domain.Entities.LeaveHolidayCalendar calendar) =>
        new
        {
            calendar.Id,
            calendar.Code,
            calendar.Name,
            calendar.Region,
            calendar.IsEnabled,
            Holidays = calendar.Holidays.Select(holiday => new
            {
                holiday.Id,
                holiday.Date,
                holiday.Name
            })
        };

    private static object ToPayrollCalendar(
        FlowDesk.Domain.Entities.LeavePayrollCalendar calendar) =>
        new
        {
            calendar.Id,
            calendar.Code,
            calendar.Name,
            calendar.TimeZoneId,
            calendar.ExpectedPeriodsInEntitlementYear,
            calendar.IsEnabled,
            Periods = calendar.Periods.Select(period => new
            {
                period.Id,
                period.StartDate,
                period.EndDate,
                period.PostingDate,
                period.EntitlementYear,
                period.PeriodNumber
            })
        };
}
