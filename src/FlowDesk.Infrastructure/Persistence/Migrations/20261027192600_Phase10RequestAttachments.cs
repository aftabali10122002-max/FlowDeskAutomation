using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10RequestAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScanStatus = table.Column<int>(type: "int", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ScannerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true),
                    StorageCleanupPending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestAttachments", x => x.Id);
                    table.CheckConstraint("CK_RequestAttachments_DeletionAudit", "([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL AND [StorageCleanupPending] = 0) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_RequestAttachments_ScanStatus", "[ScanStatus] IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_RequestAttachments_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestAttachments_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_ClientUploadId",
                table: "RequestAttachments",
                column: "ClientUploadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_DeletedByUserId",
                table: "RequestAttachments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_RequestId_DeletedAtUtc_UploadedAtUtc",
                table: "RequestAttachments",
                columns: new[] { "RequestId", "DeletedAtUtc", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_StorageCleanupPending_DeletedAtUtc",
                table: "RequestAttachments",
                columns: new[] { "StorageCleanupPending", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_StorageKey",
                table: "RequestAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_UploadedByUserId",
                table: "RequestAttachments",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestAttachments");
        }
    }
}
