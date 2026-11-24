using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAndFunctionalRemediation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedSiteId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "ExpectedStartTime",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<double>(
                name: "LeaveBalanceDays",
                table: "Users",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MaxBreakMinutes",
                table: "Sites",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.UpdateData(
                table: "Sites",
                keyColumn: "Id",
                keyValue: 1,
                column: "MaxBreakMinutes",
                value: 30.0);

            migrationBuilder.UpdateData(
                table: "Sites",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxBreakMinutes",
                value: 30.0);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AssignedSiteId", "ExpectedStartTime", "LeaveBalanceDays", "StartDate" },
                values: new object[] { null, new TimeSpan(0, 9, 0, 0, 0), 15.0, new DateTime(2026, 11, 24, 0, 0, 0, 0, DateTimeKind.Local) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedSiteId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExpectedStartTime",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LeaveBalanceDays",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MaxBreakMinutes",
                table: "Sites");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "StartDate",
                value: new DateTime(2026, 9, 7, 0, 0, 0, 0, DateTimeKind.Local));
        }
    }
}
