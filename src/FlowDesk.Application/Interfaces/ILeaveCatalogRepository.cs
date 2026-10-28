using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface ILeaveCatalogRepository
{
    Task<IReadOnlyList<LeaveWorkSchedule>> GetSchedulesAsync();
    Task<LeaveWorkSchedule> CreateScheduleAsync(
        LeaveWorkSchedule schedule,
        IReadOnlyList<LeaveWorkScheduleDay> days);
    Task<IReadOnlyList<LeaveHolidayCalendar>> GetHolidayCalendarsAsync();
    Task<LeaveHolidayCalendar> CreateHolidayCalendarAsync(
        LeaveHolidayCalendar calendar);
    Task<LeaveHoliday> AddHolidayAsync(LeaveHoliday holiday);
    Task<IReadOnlyList<LeavePayrollCalendar>> GetPayrollCalendarsAsync();
    Task<LeavePayrollCalendar> CreatePayrollCalendarAsync(
        LeavePayrollCalendar calendar);
    Task<LeavePayPeriod> AddPayPeriodAsync(LeavePayPeriod period);
    Task<IReadOnlyList<Designation>> GetDesignationsAsync();
    Task<Designation> CreateDesignationAsync(Designation designation);
}
