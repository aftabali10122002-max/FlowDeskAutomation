using FlowDesk.Domain.Entities;
using FlowDesk.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowDesk.Infrastructure.Persistence.Configurations;

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> entity)
    {
        entity.ToTable("LeaveTypes");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        entity.Property(x => x.IsPaid).IsRequired();
        entity.Property(x => x.IsEnabled).IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();

        entity.HasData(
            new
            {
                Id = 1,
                Code = "ANNUAL",
                DisplayName = "Annual Leave",
                IsPaid = true,
                IsEnabled = true
            },
            new
            {
                Id = 2,
                Code = "SICK",
                DisplayName = "Sick Leave",
                IsPaid = true,
                IsEnabled = true
            },
            new
            {
                Id = 3,
                Code = "CASUAL",
                DisplayName = "Casual Leave",
                IsPaid = true,
                IsEnabled = true
            },
            new
            {
                Id = 4,
                Code = "UNPAID",
                DisplayName = "Unpaid Leave",
                IsPaid = false,
                IsEnabled = true
            });
    }
}

    public sealed class LeavePayrollCalendarConfiguration
        : IEntityTypeConfiguration<LeavePayrollCalendar>
    {
        public void Configure(EntityTypeBuilder<LeavePayrollCalendar> entity)
        {
            entity.ToTable("LeavePayrollCalendars", table =>
                table.HasCheckConstraint(
                    "CK_LeavePayrollCalendars_ExpectedPeriods",
                    "[ExpectedPeriodsInEntitlementYear] BETWEEN 1 AND 366"));

            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExpectedPeriodsInEntitlementYear)
                .IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        }
    }

    public sealed class LeavePayPeriodConfiguration
        : IEntityTypeConfiguration<LeavePayPeriod>
    {
        public void Configure(EntityTypeBuilder<LeavePayPeriod> entity)
        {
            entity.ToTable("LeavePayPeriods", table =>
            {
                table.HasCheckConstraint(
                    "CK_LeavePayPeriods_DateRange",
                    "[EndDate] >= [StartDate] AND [PostingDate] >= [EndDate]");
                table.HasCheckConstraint(
                    "CK_LeavePayPeriods_PeriodIdentifiers",
                    "[EntitlementYear] >= 1 AND [PeriodNumber] >= 1");
            });

            entity.HasKey(x => x.Id);
            entity.Property(x => x.StartDate).HasColumnType("date");
            entity.Property(x => x.EndDate).HasColumnType("date");
            entity.Property(x => x.PostingDate).HasColumnType("date");
            entity.HasIndex(x => new
            {
                x.LeavePayrollCalendarId,
                x.EntitlementYear,
                x.PeriodNumber
            }).IsUnique();
            entity.HasIndex(x => new { x.LeavePayrollCalendarId, x.StartDate });
            entity.HasOne(x => x.LeavePayrollCalendar)
                .WithMany(x => x.Periods)
                .HasForeignKey(x => x.LeavePayrollCalendarId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    public sealed class LeaveBalanceLedgerEntryConfiguration
        : IEntityTypeConfiguration<LeaveBalanceLedgerEntry>
    {
        public void Configure(EntityTypeBuilder<LeaveBalanceLedgerEntry> entity)
        {
            entity.ToTable("LeaveBalanceLedgerEntries", table =>
            {
                table.HasCheckConstraint(
                    "CK_LeaveBalanceLedgerEntries_Type",
                    "[EntryType] BETWEEN 1 AND 11");
                table.HasCheckConstraint(
                    "CK_LeaveBalanceLedgerEntries_Units",
                    "[Units] <> 0");
            });

            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntryType).HasConversion<int>();
            entity.Property(x => x.Units).HasColumnType("decimal(18,2)");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.ExpiresOn).HasColumnType("date");
            entity.Property(x => x.EffectiveDate).HasColumnType("date");
            entity.Property(x => x.LeaveDate).HasColumnType("date");
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new
            {
                x.EmployeeUserId,
                x.LeaveTypeId,
                x.EffectiveDate,
                x.CreatedAtUtc
            });
            entity.HasIndex(x => x.RequestId);
            entity.HasIndex(x => x.SourceEntryId);
            entity.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.LeaveType)
                .WithMany()
                .HasForeignKey(x => x.LeaveTypeId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.LeavePolicy)
                .WithMany()
                .HasForeignKey(x => x.LeavePolicyId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Request)
                .WithMany()
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.PayPeriod)
                .WithMany()
                .HasForeignKey(x => x.PayPeriodId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Actor)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.SourceEntry)
                .WithMany()
                .HasForeignKey(x => x.SourceEntryId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    public sealed class LeaveBalanceAllocationConfiguration
        : IEntityTypeConfiguration<LeaveBalanceAllocation>
    {
        public void Configure(EntityTypeBuilder<LeaveBalanceAllocation> entity)
        {
            entity.ToTable("LeaveBalanceAllocations", table =>
                table.HasCheckConstraint(
                    "CK_LeaveBalanceAllocations_Units",
                    "[Units] > 0"));

            entity.HasKey(x => x.Id);
            entity.Property(x => x.LeaveDate).HasColumnType("date");
            entity.Property(x => x.Units).HasColumnType("decimal(18,2)");
            entity.HasIndex(x => new
            {
                x.LedgerEntryId,
                x.SourceEntryId,
                x.LeaveDate
            }).IsUnique();
            entity.HasOne(x => x.LedgerEntry)
                .WithMany(x => x.Allocations)
                .HasForeignKey(x => x.LedgerEntryId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.SourceEntry)
                .WithMany()
                .HasForeignKey(x => x.SourceEntryId)
                .OnDelete(DeleteBehavior.NoAction);
        }

        public sealed class LeaveRequestPolicyDayConfiguration
            : IEntityTypeConfiguration<LeaveRequestPolicyDay>
        {
            public void Configure(EntityTypeBuilder<LeaveRequestPolicyDay> entity)
            {
                entity.ToTable("LeaveRequestPolicyDays", table =>
                    table.HasCheckConstraint(
                        "CK_LeaveRequestPolicyDays_Units",
                        "[Units] > 0"));
                entity.HasKey(x => new { x.RequestId, x.LeaveDate });
                entity.Property(x => x.LeaveDate).HasColumnType("date");
                entity.Property(x => x.Units).HasColumnType("decimal(18,2)");
                entity.HasOne(x => x.Request)
                    .WithMany(x => x.LeavePolicyDays)
                    .HasForeignKey(x => x.RequestId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(x => x.LeavePolicy)
                    .WithMany()
                    .HasForeignKey(x => x.LeavePolicyId)
                    .OnDelete(DeleteBehavior.NoAction);
            }
        }
    }

public sealed class DesignationConfiguration
    : IEntityTypeConfiguration<Designation>
{
    public void Configure(EntityTypeBuilder<Designation> entity)
    {
        entity.ToTable("Designations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.IsEnabled).IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class EmployeeEmploymentProfileConfiguration
    : IEntityTypeConfiguration<EmployeeEmploymentProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeEmploymentProfile> entity)
    {
        entity.ToTable("EmployeeEmploymentProfiles", table =>
            table.HasCheckConstraint(
                "CK_EmployeeEmploymentProfiles_EffectiveRange",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));

        entity.HasKey(x => x.Id);
        entity.Property(x => x.EmploymentType).HasConversion<int?>();
        entity.Property(x => x.HireDate).HasColumnType("date");
        entity.Property(x => x.EffectiveFrom).HasColumnType("date");
        entity.Property(x => x.EffectiveTo).HasColumnType("date");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.UserId, x.EffectiveFrom }).IsUnique();
        entity.HasIndex(x => new
        {
            x.UserId,
            x.EffectiveFrom,
            x.EffectiveTo
        });
        entity.HasIndex(x => new { x.UserId, x.EffectiveTo });
        entity.HasIndex(x => x.DesignationId);

        entity.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Designation)
            .WithMany()
            .HasForeignKey(x => x.DesignationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeaveWorkScheduleConfiguration
    : IEntityTypeConfiguration<LeaveWorkSchedule>
{
    public void Configure(EntityTypeBuilder<LeaveWorkSchedule> entity)
    {
        entity.ToTable("LeaveWorkSchedules");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.IsEnabled).IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class LeaveWorkScheduleDayConfiguration
    : IEntityTypeConfiguration<LeaveWorkScheduleDay>
{
    public void Configure(EntityTypeBuilder<LeaveWorkScheduleDay> entity)
    {
        entity.ToTable("LeaveWorkScheduleDays", table =>
        {
            table.HasCheckConstraint(
                "CK_LeaveWorkScheduleDays_DayOfWeek",
                "[DayOfWeek] BETWEEN 0 AND 6");
            table.HasCheckConstraint(
                "CK_LeaveWorkScheduleDays_StandardHours",
                "[StandardHours] >= 0 AND [StandardHours] <= 24");
            table.HasCheckConstraint(
                "CK_LeaveWorkScheduleDays_WorkingDayHours",
                "([IsWorkingDay] = 1 AND [StandardHours] > 0) OR " +
                "([IsWorkingDay] = 0 AND [StandardHours] = 0)");
        });

        entity.HasKey(x => x.Id);
        entity.Property(x => x.DayOfWeek).HasConversion<int>();
        entity.Property(x => x.StandardHours).HasColumnType("decimal(4,2)");
        entity.HasIndex(x => new { x.LeaveWorkScheduleId, x.DayOfWeek })
            .IsUnique();
        entity.HasOne(x => x.LeaveWorkSchedule)
            .WithMany(x => x.Days)
            .HasForeignKey(x => x.LeaveWorkScheduleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeaveHolidayCalendarConfiguration
    : IEntityTypeConfiguration<LeaveHolidayCalendar>
{
    public void Configure(EntityTypeBuilder<LeaveHolidayCalendar> entity)
    {
        entity.ToTable("LeaveHolidayCalendars");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Region).HasMaxLength(100);
        entity.Property(x => x.IsEnabled).IsRequired();
        entity.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class LeaveHolidayConfiguration
    : IEntityTypeConfiguration<LeaveHoliday>
{
    public void Configure(EntityTypeBuilder<LeaveHoliday> entity)
    {
        entity.ToTable("LeaveHolidays");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Date).HasColumnType("date");
        entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
        entity.HasIndex(x => new { x.LeaveHolidayCalendarId, x.Date })
            .IsUnique();
        entity.HasOne(x => x.LeaveHolidayCalendar)
            .WithMany(x => x.Holidays)
            .HasForeignKey(x => x.LeaveHolidayCalendarId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeavePolicyConfiguration
    : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> entity)
    {
        entity.ToTable("LeavePolicies", table =>
        {
            table.HasCheckConstraint(
                "CK_LeavePolicies_Status",
                "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_Lifecycle",
                "([Status] = 1 AND [PublishedAtUtc] IS NULL AND " +
                "[PublishedByUserId] IS NULL AND [SupersededAtUtc] IS NULL " +
                "AND [SupersededByPolicyId] IS NULL) OR " +
                "([Status] = 2 AND [PublishedAtUtc] IS NOT NULL AND " +
                "[PublishedByUserId] IS NOT NULL AND [SupersededAtUtc] IS NULL " +
                "AND [SupersededByPolicyId] IS NULL) OR " +
                "([Status] = 3 AND [PublishedAtUtc] IS NOT NULL AND " +
                "[PublishedByUserId] IS NOT NULL AND [SupersededAtUtc] IS NOT NULL " +
                "AND [SupersededByPolicyId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_NotSelfSuperseded",
                "[SupersededByPolicyId] IS NULL OR " +
                "[SupersededByPolicyId] <> [Id]");
            table.HasCheckConstraint(
                "CK_LeavePolicies_Version",
                "[Version] > 0");
            table.HasCheckConstraint(
                "CK_LeavePolicies_ScopeTarget",
                "([Scope] = 1 AND [DepartmentId] IS NULL AND " +
                "[DesignationId] IS NULL AND [EmploymentType] IS NULL) OR " +
                "([Scope] = 2 AND [DepartmentId] IS NOT NULL AND " +
                "[DesignationId] IS NULL AND [EmploymentType] IS NULL) OR " +
                "([Scope] = 3 AND [DepartmentId] IS NULL AND " +
                "[DesignationId] IS NOT NULL AND [EmploymentType] IS NULL) OR " +
                "([Scope] = 4 AND [DepartmentId] IS NULL AND " +
                "[DesignationId] IS NULL AND [EmploymentType] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_ScopeEnum",
                "[Scope] IN (1, 2, 3, 4)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_DayCalculation",
                "([DayCalculationMethod] = 1) OR " +
                "([DayCalculationMethod] = 2 AND [LeaveWorkScheduleId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_HalfDaySettings",
                "([AllowsHalfDays] = 0 AND [HalfDayUnits] IS NULL) OR " +
                "([AllowsHalfDays] = 1 AND [HalfDayUnits] > 0 AND " +
                "[HalfDayUnits] < 1)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_AccrualSettings",
                "([IsAccrualEnabled] = 1 AND [AccrualFrequency] IN (1, 2, 3)) OR " +
                "([IsAccrualEnabled] = 0 AND [AccrualFrequency] IS NULL)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_PayrollCalendar",
                "([AccrualFrequency] = 3 AND [LeavePayrollCalendarId] IS NOT NULL) OR " +
                "([AccrualFrequency] IS NULL OR [AccrualFrequency] <> 3) AND " +
                "[LeavePayrollCalendarId] IS NULL");
            table.HasCheckConstraint(
                "CK_LeavePolicies_CarryForwardSettings",
                "([AllowsCarryForward] = 1 AND [CarryForwardCap] >= 0 AND " +
                "([CarryForwardExpiryMonths] IS NULL OR [CarryForwardExpiryMonths] > 0)) OR " +
                "([AllowsCarryForward] = 0 AND [CarryForwardCap] IS NULL AND " +
                "[CarryForwardExpiryMonths] IS NULL)");
            table.HasCheckConstraint(
                "CK_LeavePolicies_ProbationPeriod",
                "[ProbationPeriodDays] IS NULL OR [ProbationPeriodDays] > 0");
            table.HasCheckConstraint(
                "CK_LeavePolicies_EffectiveRange",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        });

        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Status)
            .HasConversion<int>()
            .HasDefaultValue(LeavePolicyStatus.Draft)
            .HasSentinel(LeavePolicyStatus.Draft)
            .IsRequired();
        entity.Property(x => x.Scope).HasConversion<int>();
        entity.Property(x => x.EmploymentType).HasConversion<int?>();
        entity.Property(x => x.PublishedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.SupersededAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.EffectiveFrom).HasColumnType("date");
        entity.Property(x => x.EffectiveTo).HasColumnType("date");
        entity.Property(x => x.DayCalculationMethod).HasConversion<int>();
        entity.Property(x => x.HalfDayUnits).HasColumnType("decimal(5,2)");
        entity.Property(x => x.AccrualFrequency).HasConversion<int?>();
        entity.Property(x => x.CarryForwardCap).HasColumnType("decimal(9,2)");
        entity.HasIndex(x => new { x.Code, x.Version })
            .IsUnique()
            .HasFilter("[Scope] = 1");
        entity.HasIndex(x => new
            {
                x.Code,
                x.Version,
                x.DepartmentId
            })
            .IsUnique()
            .HasFilter("[Scope] = 2 AND [DepartmentId] IS NOT NULL");
        entity.HasIndex(x => new
            {
                x.Code,
                x.Version,
                x.DesignationId
            })
            .IsUnique()
            .HasFilter("[Scope] = 3 AND [DesignationId] IS NOT NULL");
        entity.HasIndex(x => new
            {
                x.Code,
                x.Version,
                x.EmploymentType
            })
            .IsUnique()
            .HasFilter("[Scope] = 4 AND [EmploymentType] IS NOT NULL");
        entity.HasIndex(x => new
        {
            x.EffectiveFrom,
            x.EffectiveTo
        }).HasFilter("[Scope] = 1 AND [Status] IN (2, 3)");
        entity.HasIndex(x => new
        {
            x.DepartmentId,
            x.EffectiveFrom,
            x.EffectiveTo
        }).HasFilter("[Scope] = 2 AND [Status] IN (2, 3)");
        entity.HasIndex(x => new
        {
            x.DesignationId,
            x.EffectiveFrom,
            x.EffectiveTo
        }).HasFilter("[Scope] = 3 AND [Status] IN (2, 3)");
        entity.HasIndex(x => new
        {
            x.EmploymentType,
            x.EffectiveFrom,
            x.EffectiveTo
        }).HasFilter("[Scope] = 4 AND [Status] IN (2, 3)");
        entity.HasIndex(x => x.SupersededByPolicyId)
            .IsUnique()
            .HasFilter("[SupersededByPolicyId] IS NOT NULL");
        entity.HasIndex(x => new { x.Scope, x.EffectiveFrom, x.EffectiveTo });
        entity.HasIndex(x => x.LeaveWorkScheduleId);
        entity.HasIndex(x => x.LeaveHolidayCalendarId);
        entity.HasIndex(x => x.LeavePayrollCalendarId);

        entity.HasOne(x => x.Department)
            .WithMany()
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Designation)
            .WithMany()
            .HasForeignKey(x => x.DesignationId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.LeaveWorkSchedule)
            .WithMany()
            .HasForeignKey(x => x.LeaveWorkScheduleId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.LeaveHolidayCalendar)
            .WithMany()
            .HasForeignKey(x => x.LeaveHolidayCalendarId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.LeavePayrollCalendar)
            .WithMany()
            .HasForeignKey(x => x.LeavePayrollCalendarId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.PublishedByUser)
            .WithMany()
            .HasForeignKey(x => x.PublishedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.SupersededByPolicy)
            .WithMany()
            .HasForeignKey(x => x.SupersededByPolicyId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.Navigation(x => x.Entitlements)
            .HasField("_entitlements")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LeavePolicyEntitlementConfiguration
    : IEntityTypeConfiguration<LeavePolicyEntitlement>
{
    public void Configure(EntityTypeBuilder<LeavePolicyEntitlement> entity)
    {
        entity.ToTable("LeavePolicyEntitlements", table =>
            table.HasCheckConstraint(
                "CK_LeavePolicyEntitlements_AnnualEntitlement",
                "[AnnualEntitlement] >= 0"));

        entity.HasKey(x => x.Id);
        entity.Property(x => x.AnnualEntitlement)
            .HasColumnType("decimal(9,2)")
            .IsRequired();
        entity.HasIndex(x => new { x.LeavePolicyId, x.LeaveTypeId })
            .IsUnique();
        entity.HasOne(x => x.LeavePolicy)
            .WithMany(x => x.Entitlements)
            .HasForeignKey(x => x.LeavePolicyId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.LeaveType)
            .WithMany()
            .HasForeignKey(x => x.LeaveTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeavePolicyAssignmentConfiguration
    : IEntityTypeConfiguration<LeavePolicyAssignment>
{
    public void Configure(EntityTypeBuilder<LeavePolicyAssignment> entity)
    {
        entity.ToTable("LeavePolicyAssignments", table =>
            table.HasCheckConstraint(
                "CK_LeavePolicyAssignments_EffectiveRange",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));

        entity.HasKey(x => x.Id);
        entity.Property(x => x.EffectiveFrom).HasColumnType("date");
        entity.Property(x => x.EffectiveTo).HasColumnType("date");
        entity.HasIndex(x => new { x.UserId, x.EffectiveFrom }).IsUnique();
        entity.HasIndex(x => new
        {
            x.UserId,
            x.EffectiveFrom,
            x.EffectiveTo
        });
        entity.HasIndex(x => new { x.UserId, x.EffectiveTo });
        entity.HasIndex(x => x.LeavePolicyId);

        entity.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.LeavePolicy)
            .WithMany()
            .HasForeignKey(x => x.LeavePolicyId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.AssignedByAdminUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedByAdminUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
