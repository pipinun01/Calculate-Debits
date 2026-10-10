using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Debits.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_transactions_shop_id_created_at",
                table: "transactions",
                columns: new[] { "shop_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_shop_id_created_at",
                table: "transactions");
        }
    }
}
