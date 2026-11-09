using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerPay.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeyToTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "transfers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE transfers SET idempotency_key = id::text;");
            migrationBuilder.Sql("ALTER TABLE transfers ALTER COLUMN idempotency_key DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_idempotency_key",
                table: "transfers",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transfers_idempotency_key",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "transfers");
        }
    }
}
