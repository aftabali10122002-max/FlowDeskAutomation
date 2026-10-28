using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8ScheduledLeaveDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LeaveDate",
                table: "LeaveBalanceLedgerEntries",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaveDate",
                table: "LeaveBalanceLedgerEntries");
        }
    }
}
