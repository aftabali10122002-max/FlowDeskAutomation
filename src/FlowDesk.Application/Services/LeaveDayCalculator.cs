using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.Services;

public sealed record LeaveDateCharge(
    DateOnly Date,
    decimal Units,
    LeaveDayPortion Portion,
    LeavePolicy Policy,
    LeavePolicyEntitlement Entitlement);

public static class LeaveDayCalculator
{
    public static IReadOnlyList<LeaveDateCharge> Calculate(
        DateOnly startDate,
        DateOnly endDate,
        LeaveDayPortion startPortion,
        LeaveDayPortion endPortion,
        Func<DateOnly, LeavePolicyResolution> resolvePolicy,
        IReadOnlyDictionary<int, IReadOnlySet<DayOfWeek>> workingDaysBySchedule,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidaysByCalendar)
    {
        ArgumentNullException.ThrowIfNull(resolvePolicy);
        ArgumentNullException.ThrowIfNull(workingDaysBySchedule);
        ArgumentNullException.ThrowIfNull(holidaysByCalendar);
        if (endDate < startDate)
        {
            throw new ArgumentException(
                "The end date cannot precede the start date.");
        }

        if (!Enum.IsDefined(startPortion) || !Enum.IsDefined(endPortion))
        {
            throw new ArgumentOutOfRangeException(nameof(startPortion));
        }

        if (startDate == endDate &&
            startPortion == LeaveDayPortion.HalfDay &&
            endPortion == LeaveDayPortion.HalfDay)
        {
            throw new ArgumentException(
                "A single-day request can contain only one half-day portion.");
        }

        var charges = new List<LeaveDateCharge>();
        for (var date = startDate; ; date = date.AddDays(1))
        {
            var resolution = resolvePolicy(date);
            var policy = resolution.Policy;
            if (!IsChargeable(
                    date,
                    policy,
                    workingDaysBySchedule,
                    holidaysByCalendar))
            {
                if ((date == startDate &&
                     startPortion == LeaveDayPortion.HalfDay) ||
                    (date == endDate &&
                     endPortion == LeaveDayPortion.HalfDay))
                {
                    throw new LeavePolicyConfigurationException(
                        "A half-day cannot be requested on a non-chargeable date.");
                }

                if (date == endDate)
                {
                    break;
                }

                continue;
            }

            var portion = startDate == endDate
                ? startPortion == LeaveDayPortion.HalfDay ||
                    endPortion == LeaveDayPortion.HalfDay
                    ? LeaveDayPortion.HalfDay
                    : LeaveDayPortion.FullDay
                : date == startDate
                    ? startPortion
                    : date == endDate
                        ? endPortion
                        : LeaveDayPortion.FullDay;
            if (portion == LeaveDayPortion.HalfDay &&
                (!policy.AllowsHalfDays ||
                 policy.HalfDayUnits is not decimal halfDayUnits ||
                 halfDayUnits <= 0))
            {
                throw new LeavePolicyConfigurationException(
                    "The applicable policy does not allow half-day leave.");
            }

            var units = portion == LeaveDayPortion.HalfDay
                ? policy.HalfDayUnits!.Value
                : 1m;
            charges.Add(new LeaveDateCharge(
                date,
                LeaveAccrualCalculator.RoundUnits(units),
                portion,
                policy,
                resolution.Entitlement));
            if (date == endDate)
            {
                break;
            }
        }

        if (charges.Count == 0)
        {
            throw new LeavePolicyConfigurationException(
                "The requested date range contains no chargeable leave dates.");
        }

        return charges;
    }

    private static bool IsChargeable(
        DateOnly date,
        LeavePolicy policy,
        IReadOnlyDictionary<int, IReadOnlySet<DayOfWeek>> workingDaysBySchedule,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidaysByCalendar)
    {
        if (policy.DayCalculationMethod == LeaveDayCalculationMethod.WorkingDays)
        {
            if (policy.LeaveWorkScheduleId is not int scheduleId ||
                !workingDaysBySchedule.TryGetValue(scheduleId, out var workingDays))
            {
                throw new LeavePolicyConfigurationException(
                    "The applicable work schedule is missing or unavailable.");
            }

            if (!workingDays.Contains(date.DayOfWeek))
            {
                return false;
            }

            if (policy.LeaveHolidayCalendarId is int calendarId)
            {
                if (!holidaysByCalendar.TryGetValue(
                        calendarId,
                        out var holidays))
                {
                    throw new LeavePolicyConfigurationException(
                        "The applicable holiday calendar is missing or unavailable.");
                }

                if (holidays.Contains(date))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
