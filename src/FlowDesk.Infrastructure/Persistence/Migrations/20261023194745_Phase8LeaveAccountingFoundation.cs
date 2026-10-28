using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8LeaveAccountingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedLeaveUnits",
                table: "LeaveRequestDetails",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndDayPortion",
                table: "LeaveRequestDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "StartDayPortion",
                table: "LeaveRequestDetails",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LeavePayrollCalendarId",
                table: "LeavePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                table: "LeavePolicies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedByUserId",
                table: "LeavePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "LeavePolicies",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "LeavePolicies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersededByPolicyId",
                table: "LeavePolicies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeavePayrollCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpectedPeriodsInEntitlementYear = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePayrollCalendars", x => x.Id);
                    table.CheckConstraint("CK_LeavePayrollCalendars_ExpectedPeriods", "[ExpectedPeriodsInEntitlementYear] BETWEEN 1 AND 366");
                });

            migrationBuilder.CreateTable(
                name: "LeavePayPeriods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeavePayrollCalendarId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EntitlementYear = table.Column<int>(type: "int", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePayPeriods", x => x.Id);
                    table.CheckConstraint("CK_LeavePayPeriods_DateRange", "[EndDate] >= [StartDate] AND [PostingDate] >= [EndDate]");
                    table.CheckConstraint("CK_LeavePayPeriods_PeriodIdentifiers", "[EntitlementYear] >= 1 AND [PeriodNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_LeavePayPeriods_LeavePayrollCalendars_LeavePayrollCalendarId",
                        column: x => x.LeavePayrollCalendarId,
                        principalTable: "LeavePayrollCalendars",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaveBalanceLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeUserId = table.Column<int>(type: "int", nullable: false),
                    LeaveTypeId = table.Column<int>(type: "int", nullable: false),
                    LeavePolicyId = table.Column<int>(type: "int", nullable: true),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    PayPeriodId = table.Column<long>(type: "bigint", nullable: true),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceEntryId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalanceLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_LeaveBalanceLedgerEntries_Type", "[EntryType] BETWEEN 1 AND 11");
                    table.CheckConstraint("CK_LeaveBalanceLedgerEntries_Units", "[Units] <> 0");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_LeaveBalanceLedgerEntries_SourceEntryId",
                        column: x => x.SourceEntryId,
                        principalTable: "LeaveBalanceLedgerEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_LeavePayPeriods_PayPeriodId",
                        column: x => x.PayPeriodId,
                        principalTable: "LeavePayPeriods",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_LeavePolicies_LeavePolicyId",
                        column: x => x.LeavePolicyId,
                        principalTable: "LeavePolicies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceLedgerEntries_Users_EmployeeUserId",
                        column: x => x.EmployeeUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaveBalanceAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: false),
                    SourceEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LeaveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalanceAllocations", x => x.Id);
                    table.CheckConstraint("CK_LeaveBalanceAllocations_Units", "[Units] > 0");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceAllocations_LeaveBalanceLedgerEntries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalTable: "LeaveBalanceLedgerEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceAllocations_LeaveBalanceLedgerEntries_SourceEntryId",
                        column: x => x.SourceEntryId,
                        principalTable: "LeaveBalanceLedgerEntries",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequestDetails_CalculatedLeaveUnits",
                table: "LeaveRequestDetails",
                sql: "[CalculatedLeaveUnits] IS NULL OR [CalculatedLeaveUnits] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequestDetails_DayPortions",
                table: "LeaveRequestDetails",
                sql: "[StartDayPortion] IN (1, 2) AND [EndDayPortion] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_LeavePayrollCalendarId",
                table: "LeavePolicies",
                column: "LeavePayrollCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_PublishedByUserId",
                table: "LeavePolicies",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_SupersededByPolicyId",
                table: "LeavePolicies",
                column: "SupersededByPolicyId",
                unique: true,
                filter: "[SupersededByPolicyId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeavePolicies_Lifecycle",
                table: "LeavePolicies",
                sql: "([Status] = 1 AND [PublishedAtUtc] IS NULL AND [PublishedByUserId] IS NULL AND [SupersededAtUtc] IS NULL AND [SupersededByPolicyId] IS NULL) OR ([Status] = 2 AND [PublishedAtUtc] IS NOT NULL AND [PublishedByUserId] IS NOT NULL AND [SupersededAtUtc] IS NULL AND [SupersededByPolicyId] IS NULL) OR ([Status] = 3 AND [PublishedAtUtc] IS NOT NULL AND [PublishedByUserId] IS NOT NULL AND [SupersededAtUtc] IS NOT NULL AND [SupersededByPolicyId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeavePolicies_NotSelfSuperseded",
                table: "LeavePolicies",
                sql: "[SupersededByPolicyId] IS NULL OR [SupersededByPolicyId] <> [Id]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeavePolicies_PayrollCalendar",
                table: "LeavePolicies",
                sql: "([AccrualFrequency] = 3 AND [LeavePayrollCalendarId] IS NOT NULL) OR ([AccrualFrequency] IS NULL OR [AccrualFrequency] <> 3) AND [LeavePayrollCalendarId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeavePolicies_Status",
                table: "LeavePolicies",
                sql: "[Status] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceAllocations_LedgerEntryId_SourceEntryId_LeaveDate",
                table: "LeaveBalanceAllocations",
                columns: new[] { "LedgerEntryId", "SourceEntryId", "LeaveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceAllocations_SourceEntryId",
                table: "LeaveBalanceAllocations",
                column: "SourceEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_ActorUserId",
                table: "LeaveBalanceLedgerEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_CreatedAtUtc",
                table: "LeaveBalanceLedgerEntries",
                columns: new[] { "EmployeeUserId", "LeaveTypeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_IdempotencyKey",
                table: "LeaveBalanceLedgerEntries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_LeavePolicyId",
                table: "LeaveBalanceLedgerEntries",
                column: "LeavePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_LeaveTypeId",
                table: "LeaveBalanceLedgerEntries",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_PayPeriodId",
                table: "LeaveBalanceLedgerEntries",
                column: "PayPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_RequestId",
                table: "LeaveBalanceLedgerEntries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_SourceEntryId",
                table: "LeaveBalanceLedgerEntries",
                column: "SourceEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePayPeriods_LeavePayrollCalendarId_EntitlementYear_PeriodNumber",
                table: "LeavePayPeriods",
                columns: new[] { "LeavePayrollCalendarId", "EntitlementYear", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePayPeriods_LeavePayrollCalendarId_StartDate",
                table: "LeavePayPeriods",
                columns: new[] { "LeavePayrollCalendarId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePayrollCalendars_Code",
                table: "LeavePayrollCalendars",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LeavePolicies_LeavePayrollCalendars_LeavePayrollCalendarId",
                table: "LeavePolicies",
                column: "LeavePayrollCalendarId",
                principalTable: "LeavePayrollCalendars",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LeavePolicies_LeavePolicies_SupersededByPolicyId",
                table: "LeavePolicies",
                column: "SupersededByPolicyId",
                principalTable: "LeavePolicies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LeavePolicies_Users_PublishedByUserId",
                table: "LeavePolicies",
                column: "PublishedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeavePolicies_LeavePayrollCalendars_LeavePayrollCalendarId",
                table: "LeavePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_LeavePolicies_LeavePolicies_SupersededByPolicyId",
                table: "LeavePolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_LeavePolicies_Users_PublishedByUserId",
                table: "LeavePolicies");

            migrationBuilder.DropTable(
                name: "LeaveBalanceAllocations");

            migrationBuilder.DropTable(
                name: "LeaveBalanceLedgerEntries");

            migrationBuilder.DropTable(
                name: "LeavePayPeriods");

            migrationBuilder.DropTable(
                name: "LeavePayrollCalendars");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequestDetails_CalculatedLeaveUnits",
                table: "LeaveRequestDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequestDetails_DayPortions",
                table: "LeaveRequestDetails");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_LeavePayrollCalendarId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_PublishedByUserId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_SupersededByPolicyId",
                table: "LeavePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeavePolicies_Lifecycle",
                table: "LeavePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeavePolicies_NotSelfSuperseded",
                table: "LeavePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeavePolicies_PayrollCalendar",
                table: "LeavePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeavePolicies_Status",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "CalculatedLeaveUnits",
                table: "LeaveRequestDetails");

            migrationBuilder.DropColumn(
                name: "EndDayPortion",
                table: "LeaveRequestDetails");

            migrationBuilder.DropColumn(
                name: "StartDayPortion",
                table: "LeaveRequestDetails");

            migrationBuilder.DropColumn(
                name: "LeavePayrollCalendarId",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "PublishedByUserId",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                table: "LeavePolicies");

            migrationBuilder.DropColumn(
                name: "SupersededByPolicyId",
                table: "LeavePolicies");
        }
    }
}
