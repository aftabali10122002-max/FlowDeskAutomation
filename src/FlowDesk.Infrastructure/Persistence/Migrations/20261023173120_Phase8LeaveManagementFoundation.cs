using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8LeaveManagementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveHolidayCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveHolidayCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveWorkSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveWorkSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeEmploymentProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DesignationId = table.Column<int>(type: "int", nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: true),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEmploymentProfiles", x => x.Id);
                    table.CheckConstraint("CK_EmployeeEmploymentProfiles_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentProfiles_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmployeeEmploymentProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaveHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeaveHolidayCalendarId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveHolidays_LeaveHolidayCalendars_LeaveHolidayCalendarId",
                        column: x => x.LeaveHolidayCalendarId,
                        principalTable: "LeaveHolidayCalendars",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    DesignationId = table.Column<int>(type: "int", nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DayCalculationMethod = table.Column<int>(type: "int", nullable: false),
                    LeaveWorkScheduleId = table.Column<int>(type: "int", nullable: true),
                    LeaveHolidayCalendarId = table.Column<int>(type: "int", nullable: true),
                    AllowsHalfDays = table.Column<bool>(type: "bit", nullable: false),
                    HalfDayUnits = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    IsAccrualEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccrualFrequency = table.Column<int>(type: "int", nullable: true),
                    AllowsCarryForward = table.Column<bool>(type: "bit", nullable: false),
                    CarryForwardCap = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    CarryForwardExpiryMonths = table.Column<int>(type: "int", nullable: true),
                    ProbationPeriodDays = table.Column<int>(type: "int", nullable: true),
                    AllowsLeaveDuringProbation = table.Column<bool>(type: "bit", nullable: false),
                    AllowsNegativeBalance = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicies", x => x.Id);
                    table.CheckConstraint("CK_LeavePolicies_AccrualSettings", "([IsAccrualEnabled] = 1 AND [AccrualFrequency] IN (1, 2, 3)) OR ([IsAccrualEnabled] = 0 AND [AccrualFrequency] IS NULL)");
                    table.CheckConstraint("CK_LeavePolicies_CarryForwardSettings", "([AllowsCarryForward] = 1 AND [CarryForwardCap] >= 0 AND ([CarryForwardExpiryMonths] IS NULL OR [CarryForwardExpiryMonths] > 0)) OR ([AllowsCarryForward] = 0 AND [CarryForwardCap] IS NULL AND [CarryForwardExpiryMonths] IS NULL)");
                    table.CheckConstraint("CK_LeavePolicies_DayCalculation", "([DayCalculationMethod] = 1) OR ([DayCalculationMethod] = 2 AND [LeaveWorkScheduleId] IS NOT NULL)");
                    table.CheckConstraint("CK_LeavePolicies_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_LeavePolicies_HalfDaySettings", "([AllowsHalfDays] = 0 AND [HalfDayUnits] IS NULL) OR ([AllowsHalfDays] = 1 AND [HalfDayUnits] > 0 AND [HalfDayUnits] < 1)");
                    table.CheckConstraint("CK_LeavePolicies_ProbationPeriod", "[ProbationPeriodDays] IS NULL OR [ProbationPeriodDays] > 0");
                    table.CheckConstraint("CK_LeavePolicies_ScopeEnum", "[Scope] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_LeavePolicies_ScopeTarget", "([Scope] = 1 AND [DepartmentId] IS NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NULL) OR ([Scope] = 2 AND [DepartmentId] IS NOT NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NULL) OR ([Scope] = 3 AND [DepartmentId] IS NULL AND [DesignationId] IS NOT NULL AND [EmploymentType] IS NULL) OR ([Scope] = 4 AND [DepartmentId] IS NULL AND [DesignationId] IS NULL AND [EmploymentType] IS NOT NULL)");
                    table.CheckConstraint("CK_LeavePolicies_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_LeavePolicies_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicies_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicies_LeaveHolidayCalendars_LeaveHolidayCalendarId",
                        column: x => x.LeaveHolidayCalendarId,
                        principalTable: "LeaveHolidayCalendars",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicies_LeaveWorkSchedules_LeaveWorkScheduleId",
                        column: x => x.LeaveWorkScheduleId,
                        principalTable: "LeaveWorkSchedules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaveWorkScheduleDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeaveWorkScheduleId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    StandardHours = table.Column<decimal>(type: "decimal(4,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveWorkScheduleDays", x => x.Id);
                    table.CheckConstraint("CK_LeaveWorkScheduleDays_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_LeaveWorkScheduleDays_StandardHours", "[StandardHours] >= 0 AND [StandardHours] <= 24");
                    table.CheckConstraint("CK_LeaveWorkScheduleDays_WorkingDayHours", "([IsWorkingDay] = 1 AND [StandardHours] > 0) OR ([IsWorkingDay] = 0 AND [StandardHours] = 0)");
                    table.ForeignKey(
                        name: "FK_LeaveWorkScheduleDays_LeaveWorkSchedules_LeaveWorkScheduleId",
                        column: x => x.LeaveWorkScheduleId,
                        principalTable: "LeaveWorkSchedules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicyAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LeavePolicyId = table.Column<int>(type: "int", nullable: false),
                    AssignedByAdminUserId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyAssignments", x => x.Id);
                    table.CheckConstraint("CK_LeavePolicyAssignments_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_LeavePolicyAssignments_LeavePolicies_LeavePolicyId",
                        column: x => x.LeavePolicyId,
                        principalTable: "LeavePolicies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicyAssignments_Users_AssignedByAdminUserId",
                        column: x => x.AssignedByAdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicyAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicyEntitlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeavePolicyId = table.Column<int>(type: "int", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "int", nullable: false),
                    AnnualEntitlement = table.Column<decimal>(type: "decimal(9,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicyEntitlements", x => x.Id);
                    table.CheckConstraint("CK_LeavePolicyEntitlements_AnnualEntitlement", "[AnnualEntitlement] >= 0");
                    table.ForeignKey(
                        name: "FK_LeavePolicyEntitlements_LeavePolicies_LeavePolicyId",
                        column: x => x.LeavePolicyId,
                        principalTable: "LeavePolicies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeavePolicyEntitlements_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "LeaveTypes",
                columns: new[] { "Id", "Code", "DisplayName", "IsEnabled", "IsPaid" },
                values: new object[,]
                {
                    { 1, "ANNUAL", "Annual Leave", true, true },
                    { 2, "SICK", "Sick Leave", true, true },
                    { 3, "CASUAL", "Casual Leave", true, true },
                    { 4, "UNPAID", "Unpaid Leave", true, false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Designations_Code",
                table: "Designations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_Name",
                table: "Designations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentProfiles_DesignationId",
                table: "EmployeeEmploymentProfiles",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentProfiles_UserId_EffectiveFrom",
                table: "EmployeeEmploymentProfiles",
                columns: new[] { "UserId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentProfiles_UserId_EffectiveTo",
                table: "EmployeeEmploymentProfiles",
                columns: new[] { "UserId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveHolidayCalendars_Code",
                table: "LeaveHolidayCalendars",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveHolidays_LeaveHolidayCalendarId_Date",
                table: "LeaveHolidays",
                columns: new[] { "LeaveHolidayCalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Code_Version",
                table: "LeavePolicies",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_DepartmentId",
                table: "LeavePolicies",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_DesignationId",
                table: "LeavePolicies",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_LeaveHolidayCalendarId",
                table: "LeavePolicies",
                column: "LeaveHolidayCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_LeaveWorkScheduleId",
                table: "LeavePolicies",
                column: "LeaveWorkScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Scope_DepartmentId_DesignationId_EmploymentType_EffectiveFrom",
                table: "LeavePolicies",
                columns: new[] { "Scope", "DepartmentId", "DesignationId", "EmploymentType", "EffectiveFrom" },
                unique: true,
                filter: "[DepartmentId] IS NOT NULL AND [DesignationId] IS NOT NULL AND [EmploymentType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Scope_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies",
                columns: new[] { "Scope", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyAssignments_AssignedByAdminUserId",
                table: "LeavePolicyAssignments",
                column: "AssignedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyAssignments_LeavePolicyId",
                table: "LeavePolicyAssignments",
                column: "LeavePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyAssignments_UserId_EffectiveFrom",
                table: "LeavePolicyAssignments",
                columns: new[] { "UserId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyAssignments_UserId_EffectiveTo",
                table: "LeavePolicyAssignments",
                columns: new[] { "UserId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyEntitlements_LeavePolicyId_LeaveTypeId",
                table: "LeavePolicyEntitlements",
                columns: new[] { "LeavePolicyId", "LeaveTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyEntitlements_LeaveTypeId",
                table: "LeavePolicyEntitlements",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_Code",
                table: "LeaveTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveWorkScheduleDays_LeaveWorkScheduleId_DayOfWeek",
                table: "LeaveWorkScheduleDays",
                columns: new[] { "LeaveWorkScheduleId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveWorkSchedules_Code",
                table: "LeaveWorkSchedules",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeEmploymentProfiles");

            migrationBuilder.DropTable(
                name: "LeaveHolidays");

            migrationBuilder.DropTable(
                name: "LeavePolicyAssignments");

            migrationBuilder.DropTable(
                name: "LeavePolicyEntitlements");

            migrationBuilder.DropTable(
                name: "LeaveWorkScheduleDays");

            migrationBuilder.DropTable(
                name: "LeavePolicies");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "LeaveHolidayCalendars");

            migrationBuilder.DropTable(
                name: "LeaveWorkSchedules");
        }
    }
}
