using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerPay.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKindToReconciliationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "finished_at",
                table: "reconciliation_runs",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "reconciliation_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_runs_day",
                table: "reconciliation_runs",
                column: "day",
                unique: true,
                filter: "kind = 'Scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reconciliation_runs_day",
                table: "reconciliation_runs");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "reconciliation_runs");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "finished_at",
                table: "reconciliation_runs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
