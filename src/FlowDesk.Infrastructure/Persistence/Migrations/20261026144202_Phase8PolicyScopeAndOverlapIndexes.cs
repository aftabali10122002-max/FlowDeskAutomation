using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8PolicyScopeAndOverlapIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Code_Version",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_DepartmentId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_DesignationId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Scope_DepartmentId_DesignationId_EmploymentType_EffectiveFrom",
                table: "LeavePolicies");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicyAssignments_UserId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicyAssignments",
                columns: new[] { "UserId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Code_Version",
                table: "LeavePolicies",
                columns: new[] { "Code", "Version" },
                unique: true,
                filter: "[Scope] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Code_Version_DepartmentId",
                table: "LeavePolicies",
                columns: new[] { "Code", "Version", "DepartmentId" },
                unique: true,
                filter: "[Scope] = 2 AND [DepartmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Code_Version_DesignationId",
                table: "LeavePolicies",
                columns: new[] { "Code", "Version", "DesignationId" },
                unique: true,
                filter: "[Scope] = 3 AND [DesignationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_Code_Version_EmploymentType",
                table: "LeavePolicies",
                columns: new[] { "Code", "Version", "EmploymentType" },
                unique: true,
                filter: "[Scope] = 4 AND [EmploymentType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_DepartmentId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies",
                columns: new[] { "DepartmentId", "EffectiveFrom", "EffectiveTo" },
                filter: "[Scope] = 2 AND [Status] IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_DesignationId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies",
                columns: new[] { "DesignationId", "EffectiveFrom", "EffectiveTo" },
                filter: "[Scope] = 3 AND [Status] IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies",
                columns: new[] { "EffectiveFrom", "EffectiveTo" },
                filter: "[Scope] = 1 AND [Status] IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_EmploymentType_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies",
                columns: new[] { "EmploymentType", "EffectiveFrom", "EffectiveTo" },
                filter: "[Scope] = 4 AND [Status] IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEmploymentProfiles_UserId_EffectiveFrom_EffectiveTo",
                table: "EmployeeEmploymentProfiles",
                columns: new[] { "UserId", "EffectiveFrom", "EffectiveTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeavePolicyAssignments_UserId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicyAssignments");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Code_Version",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Code_Version_DepartmentId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Code_Version_DesignationId",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_Code_Version_EmploymentType",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_DepartmentId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_DesignationId_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_EmploymentType_EffectiveFrom_EffectiveTo",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEmploymentProfiles_UserId_EffectiveFrom_EffectiveTo",
                table: "EmployeeEmploymentProfiles");

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
                name: "IX_LeavePolicies_Scope_DepartmentId_DesignationId_EmploymentType_EffectiveFrom",
                table: "LeavePolicies",
                columns: new[] { "Scope", "DepartmentId", "DesignationId", "EmploymentType", "EffectiveFrom" },
                unique: true,
                filter: "[DepartmentId] IS NOT NULL AND [DesignationId] IS NOT NULL AND [EmploymentType] IS NOT NULL");
        }
    }
}
