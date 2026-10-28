using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeSelfServicePortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageStorageKey",
                table: "Users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestTypeId",
                table: "ApprovalWorkflowRules",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "ApprovalRequests",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<int>(
                name: "RequestTypeId",
                table: "ApprovalRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseReimbursementRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BusinessJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseReimbursementRequestDetails", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_ExpenseReimbursementRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ItAccessRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    SystemApplication = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RequestedAccessLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: true),
                    BusinessJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItAccessRequestDetails", x => x.RequestId);
                    table.CheckConstraint("CK_ItAccessRequestDetails_DurationDays", "[DurationDays] IS NULL OR [DurationDays] > 0");
                    table.ForeignKey(
                        name: "FK_ItAccessRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    LeaveType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TotalLeaveDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequestDetails", x => x.RequestId);
                    table.CheckConstraint("CK_LeaveRequestDetails_DateRange", "[EndDate] >= [StartDate]");
                    table.CheckConstraint("CK_LeaveRequestDetails_TotalLeaveDays", "[TotalLeaveDays] > 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MaterialPurchaseRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BusinessJustification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialPurchaseRequestDetails", x => x.RequestId);
                    table.CheckConstraint("CK_MaterialPurchaseRequestDetails_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_MaterialPurchaseRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequiresAmount = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TravelRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BusinessPurpose = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelRequestDetails", x => x.RequestId);
                    table.CheckConstraint("CK_TravelRequestDetails_DateRange", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_TravelRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkFromHomeRequestDetails",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkFromHomeRequestDetails", x => x.RequestId);
                    table.CheckConstraint("CK_WorkFromHomeRequestDetails_DateRange", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_WorkFromHomeRequestDetails_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "RequestTypes",
                columns: new[] { "Id", "Code", "DisplayName", "IsEnabled", "RequiresAmount" },
                values: new object[,]
                {
                    { 1, "MATERIAL_PURCHASE", "Material Purchase", true, true },
                    { 2, "LEAVE", "Leave Request", true, false },
                    { 3, "EXPENSE_REIMBURSEMENT", "Expense Reimbursement", true, true },
                    { 4, "IT_ACCESS", "IT Access", true, false },
                    { 5, "TRAVEL", "Travel Request", true, true },
                    { 6, "WORK_FROM_HOME", "Work From Home", true, false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRules_RequestTypeId",
                table: "ApprovalWorkflowRules",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RequestTypeId",
                table: "ApprovalRequests",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_Code",
                table: "RequestTypes",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_RequestTypes_RequestTypeId",
                table: "ApprovalRequests",
                column: "RequestTypeId",
                principalTable: "RequestTypes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalWorkflowRules_RequestTypes_RequestTypeId",
                table: "ApprovalWorkflowRules",
                column: "RequestTypeId",
                principalTable: "RequestTypes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequests_RequestTypes_RequestTypeId",
                table: "ApprovalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalWorkflowRules_RequestTypes_RequestTypeId",
                table: "ApprovalWorkflowRules");

            migrationBuilder.DropTable(
                name: "ExpenseReimbursementRequestDetails");

            migrationBuilder.DropTable(
                name: "ItAccessRequestDetails");

            migrationBuilder.DropTable(
                name: "LeaveRequestDetails");

            migrationBuilder.DropTable(
                name: "MaterialPurchaseRequestDetails");

            migrationBuilder.DropTable(
                name: "RequestTypes");

            migrationBuilder.DropTable(
                name: "TravelRequestDetails");

            migrationBuilder.DropTable(
                name: "WorkFromHomeRequestDetails");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalWorkflowRules_RequestTypeId",
                table: "ApprovalWorkflowRules");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequests_RequestTypeId",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfileImageStorageKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RequestTypeId",
                table: "ApprovalWorkflowRules");

            migrationBuilder.DropColumn(
                name: "RequestTypeId",
                table: "ApprovalRequests");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "ApprovalRequests",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);
        }
    }
}
