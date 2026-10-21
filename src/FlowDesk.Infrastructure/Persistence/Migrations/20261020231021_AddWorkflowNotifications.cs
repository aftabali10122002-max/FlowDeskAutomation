using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeduplicationKey",
                table: "WorkflowNotifications",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestId",
                table: "WorkflowNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "WorkflowNotifications",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Escalation");

            migrationBuilder.CreateTable(
                name: "WorkflowEmailOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    NotificationId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEmailOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEmailOutbox_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowEmailOutbox_WorkflowNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "WorkflowNotifications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotifications_DeduplicationKey",
                table: "WorkflowNotifications",
                column: "DeduplicationKey",
                unique: true,
                filter: "[DeduplicationKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotifications_RequestId",
                table: "WorkflowNotifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEmailOutbox_DeduplicationKey",
                table: "WorkflowEmailOutbox",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEmailOutbox_NotificationId",
                table: "WorkflowEmailOutbox",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEmailOutbox_RecipientUserId",
                table: "WorkflowEmailOutbox",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEmailOutbox_SentAtUtc_LeaseUntilUtc_CreatedAtUtc",
                table: "WorkflowEmailOutbox",
                columns: new[] { "SentAtUtc", "LeaseUntilUtc", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowNotifications_ApprovalRequests_RequestId",
                table: "WorkflowNotifications",
                column: "RequestId",
                principalTable: "ApprovalRequests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowNotifications_ApprovalRequests_RequestId",
                table: "WorkflowNotifications");

            migrationBuilder.DropTable(
                name: "WorkflowEmailOutbox");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowNotifications_DeduplicationKey",
                table: "WorkflowNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowNotifications_RequestId",
                table: "WorkflowNotifications");

            migrationBuilder.DropColumn(
                name: "DeduplicationKey",
                table: "WorkflowNotifications");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "WorkflowNotifications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WorkflowNotifications");
        }
    }
}
