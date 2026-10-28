using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8LeaveWorkflowAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_CreatedAtUtc",
                table: "LeaveBalanceLedgerEntries");

            migrationBuilder.AddColumn<int>(
                name: "CancellationOfRequestId",
                table: "LeaveRequestDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveDate",
                table: "LeaveBalanceLedgerEntries",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [LeaveBalanceLedgerEntries] " +
                "SET [EffectiveDate] = CONVERT(date, [CreatedAtUtc]) " +
                "WHERE [EffectiveDate] IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EffectiveDate",
                table: "LeaveBalanceLedgerEntries",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "LeaveRequestPolicyDays",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    LeaveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LeavePolicyId = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllowsNegativeBalance = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestPolicyDays", x => new { x.RequestId, x.LeaveDate });
                    table.CheckConstraint("CK_LeaveRequestPolicyDays_Units", "[Units] > 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequestPolicyDays_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaveRequestPolicyDays_LeavePolicies_LeavePolicyId",
                        column: x => x.LeavePolicyId,
                        principalTable: "LeavePolicies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestDetails_CancellationOfRequestId",
                table: "LeaveRequestDetails",
                column: "CancellationOfRequestId",
                unique: true,
                filter: "[CancellationOfRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestDetails_ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails",
                column: "ConfiguredLeaveTypeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveRequestDetails_CancellationOfRequest",
                table: "LeaveRequestDetails",
                sql: "[CancellationOfRequestId] IS NULL OR [CancellationOfRequestId] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_EffectiveDate_CreatedAtUtc",
                table: "LeaveBalanceLedgerEntries",
                columns: new[] { "EmployeeUserId", "LeaveTypeId", "EffectiveDate", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequestPolicyDays_LeavePolicyId",
                table: "LeaveRequestPolicyDays",
                column: "LeavePolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequestDetails_ApprovalRequests_CancellationOfRequestId",
                table: "LeaveRequestDetails",
                column: "CancellationOfRequestId",
                principalTable: "ApprovalRequests",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequestDetails_LeaveTypes_ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails",
                column: "ConfiguredLeaveTypeId",
                principalTable: "LeaveTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequestDetails_ApprovalRequests_CancellationOfRequestId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequestDetails_LeaveTypes_ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropTable(
                name: "LeaveRequestPolicyDays");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequestDetails_CancellationOfRequestId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequestDetails_ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveRequestDetails_CancellationOfRequest",
                table: "LeaveRequestDetails");

            migrationBuilder.DropIndex(
                name: "IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_EffectiveDate_CreatedAtUtc",
                table: "LeaveBalanceLedgerEntries");

            migrationBuilder.DropColumn(
                name: "CancellationOfRequestId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropColumn(
                name: "ConfiguredLeaveTypeId",
                table: "LeaveRequestDetails");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "LeaveBalanceLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_CreatedAtUtc",
                table: "LeaveBalanceLedgerEntries",
                columns: new[] { "EmployeeUserId", "LeaveTypeId", "CreatedAtUtc" });
        }
    }
}
