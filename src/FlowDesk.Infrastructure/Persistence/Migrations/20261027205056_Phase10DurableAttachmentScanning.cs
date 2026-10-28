using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10DurableAttachmentScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastScanAttemptAtUtc",
                table: "RequestAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextScanAttemptAtUtc",
                table: "RequestAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScanAttempts",
                table: "RequestAttachments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScanFailureCode",
                table: "RequestAttachments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScanLeaseExpiresAtUtc",
                table: "RequestAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScanLeaseToken",
                table: "RequestAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestAttachments_ScanStatus_NextScanAttemptAtUtc_ScanLeaseExpiresAtUtc_Id",
                table: "RequestAttachments",
                columns: new[] { "ScanStatus", "NextScanAttemptAtUtc", "ScanLeaseExpiresAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RequestAttachments_ScanAttempts",
                table: "RequestAttachments",
                sql: "[ScanAttempts] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestAttachments_ScanStatus_NextScanAttemptAtUtc_ScanLeaseExpiresAtUtc_Id",
                table: "RequestAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RequestAttachments_ScanAttempts",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "LastScanAttemptAtUtc",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "NextScanAttemptAtUtc",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "ScanAttempts",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "ScanFailureCode",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "ScanLeaseExpiresAtUtc",
                table: "RequestAttachments");

            migrationBuilder.DropColumn(
                name: "ScanLeaseToken",
                table: "RequestAttachments");
        }
    }
}
