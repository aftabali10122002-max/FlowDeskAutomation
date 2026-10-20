using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalWorkflowPhase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "ApprovalRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WorkflowStageId",
                table: "ApprovalAssignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    MinimumAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaximumAmountExclusive = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowRules", x => x.Id);
                    table.CheckConstraint("CK_ApprovalWorkflowRules_AmountRange", "[MinimumAmount] >= 0 AND ([MaximumAmountExclusive] IS NULL OR [MaximumAmountExclusive] > [MinimumAmount])");
                    table.CheckConstraint("CK_ApprovalWorkflowRules_Priority", "[Priority] IS NULL OR [Priority] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_ApprovalWorkflowRules_Status", "[Status] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowRules_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    WorkflowRuleId = table.Column<int>(type: "int", nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowInstances", x => x.Id);
                    table.CheckConstraint("CK_ApprovalWorkflowInstances_Status", "[Status] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowInstances_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowInstances_ApprovalWorkflowRules_WorkflowRuleId",
                        column: x => x.WorkflowRuleId,
                        principalTable: "ApprovalWorkflowRules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowRuleStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowRuleId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ApproverType = table.Column<int>(type: "int", nullable: false),
                    ApproverDepartmentId = table.Column<int>(type: "int", nullable: true),
                    SpecificApproverUserId = table.Column<int>(type: "int", nullable: true),
                    DeadlineMinutes = table.Column<int>(type: "int", nullable: true),
                    EscalationAfterMinutes = table.Column<int>(type: "int", nullable: true),
                    EscalationApproverUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowRuleStages", x => x.Id);
                    table.CheckConstraint("CK_ApprovalWorkflowRuleStages_ApproverType", "[ApproverType] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ApprovalWorkflowRuleStages_Deadline", "[DeadlineMinutes] IS NULL OR [DeadlineMinutes] > 0");
                    table.CheckConstraint("CK_ApprovalWorkflowRuleStages_Escalation", "[EscalationAfterMinutes] IS NULL OR ([EscalationAfterMinutes] >= [DeadlineMinutes] AND [DeadlineMinutes] IS NOT NULL AND [EscalationApproverUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ApprovalWorkflowRuleStages_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowRuleStages_ApprovalWorkflowRules_WorkflowRuleId",
                        column: x => x.WorkflowRuleId,
                        principalTable: "ApprovalWorkflowRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowRuleStages_Departments_ApproverDepartmentId",
                        column: x => x.ApproverDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowRuleStages_Users_EscalationApproverUserId",
                        column: x => x.EscalationApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowRuleStages_Users_SpecificApproverUserId",
                        column: x => x.SpecificApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowStages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApproverUserId = table.Column<int>(type: "int", nullable: false),
                    DeadlineMinutes = table.Column<int>(type: "int", nullable: true),
                    EscalationAfterMinutes = table.Column<int>(type: "int", nullable: true),
                    EscalationApproverUserId = table.Column<int>(type: "int", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowStages", x => x.Id);
                    table.CheckConstraint("CK_ApprovalWorkflowStages_Sequence", "[Sequence] > 0");
                    table.CheckConstraint("CK_ApprovalWorkflowStages_Status", "[Status] IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowStages_ApprovalWorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "ApprovalWorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowStages_Users_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowStages_Users_EscalationApproverUserId",
                        column: x => x.EscalationApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisionHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowStageId = table.Column<long>(type: "bigint", nullable: false),
                    ManagerUserId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisionHistory", x => x.Id);
                    table.CheckConstraint("CK_ApprovalDecisionHistory_Decision", "[Decision] IN (3, 4)");
                    table.ForeignKey(
                        name: "FK_ApprovalDecisionHistory_ApprovalWorkflowStages_WorkflowStageId",
                        column: x => x.WorkflowStageId,
                        principalTable: "ApprovalWorkflowStages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalDecisionHistory_Users_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowEscalations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowStageId = table.Column<long>(type: "bigint", nullable: false),
                    EscalationNumber = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowEscalations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowEscalations_ApprovalWorkflowStages_WorkflowStageId",
                        column: x => x.WorkflowStageId,
                        principalTable: "ApprovalWorkflowStages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowEscalations_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    EscalationId = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowNotifications_ApprovalWorkflowEscalations_EscalationId",
                        column: x => x.EscalationId,
                        principalTable: "ApprovalWorkflowEscalations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalRequests_Priority",
                table: "ApprovalRequests",
                sql: "[Priority] IS NULL OR [Priority] IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAssignments_WorkflowStageId",
                table: "ApprovalAssignments",
                column: "WorkflowStageId",
                unique: true,
                filter: "[WorkflowStageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisionHistory_ManagerUserId",
                table: "ApprovalDecisionHistory",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisionHistory_WorkflowStageId",
                table: "ApprovalDecisionHistory",
                column: "WorkflowStageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowEscalations_RecipientUserId",
                table: "ApprovalWorkflowEscalations",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowEscalations_WorkflowStageId_EscalationNumber",
                table: "ApprovalWorkflowEscalations",
                columns: new[] { "WorkflowStageId", "EscalationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowInstances_RequestId",
                table: "ApprovalWorkflowInstances",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowInstances_WorkflowRuleId_RuleVersion",
                table: "ApprovalWorkflowInstances",
                columns: new[] { "WorkflowRuleId", "RuleVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRules_DepartmentId",
                table: "ApprovalWorkflowRules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRules_Status_DepartmentId_MinimumAmount_MaximumAmountExclusive_Priority",
                table: "ApprovalWorkflowRules",
                columns: new[] { "Status", "DepartmentId", "MinimumAmount", "MaximumAmountExclusive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRules_WorkflowKey_Version",
                table: "ApprovalWorkflowRules",
                columns: new[] { "WorkflowKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRuleStages_ApproverDepartmentId",
                table: "ApprovalWorkflowRuleStages",
                column: "ApproverDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRuleStages_EscalationApproverUserId",
                table: "ApprovalWorkflowRuleStages",
                column: "EscalationApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRuleStages_SpecificApproverUserId",
                table: "ApprovalWorkflowRuleStages",
                column: "SpecificApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowRuleStages_WorkflowRuleId_Sequence",
                table: "ApprovalWorkflowRuleStages",
                columns: new[] { "WorkflowRuleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_ApproverUserId",
                table: "ApprovalWorkflowStages",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_EscalationApproverUserId",
                table: "ApprovalWorkflowStages",
                column: "EscalationApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_Status_DueAtUtc",
                table: "ApprovalWorkflowStages",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_WorkflowInstanceId",
                table: "ApprovalWorkflowStages",
                column: "WorkflowInstanceId",
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_WorkflowInstanceId_Sequence",
                table: "ApprovalWorkflowStages",
                columns: new[] { "WorkflowInstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotifications_EscalationId",
                table: "WorkflowNotifications",
                column: "EscalationId",
                unique: true,
                filter: "[EscalationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNotifications_RecipientUserId",
                table: "WorkflowNotifications",
                column: "RecipientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalAssignments_ApprovalWorkflowStages_WorkflowStageId",
                table: "ApprovalAssignments",
                column: "WorkflowStageId",
                principalTable: "ApprovalWorkflowStages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalAssignments_ApprovalWorkflowStages_WorkflowStageId",
                table: "ApprovalAssignments");

            migrationBuilder.DropTable(
                name: "ApprovalDecisionHistory");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowRuleStages");

            migrationBuilder.DropTable(
                name: "WorkflowNotifications");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowEscalations");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowStages");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowInstances");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalRequests_Priority",
                table: "ApprovalRequests");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalAssignments_WorkflowStageId",
                table: "ApprovalAssignments");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "WorkflowStageId",
                table: "ApprovalAssignments");
        }
    }
}
