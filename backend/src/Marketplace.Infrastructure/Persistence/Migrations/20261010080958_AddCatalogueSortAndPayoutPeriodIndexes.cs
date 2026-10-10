using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueSortAndPayoutPeriodIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_seller_payouts_period_end",
                table: "seller_payouts",
                column: "PeriodEnd");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "ix_products_popularity",
                table: "products",
                columns: new[] { "SoldCount", "ViewCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_seller_payouts_period_end",
                table: "seller_payouts");

            migrationBuilder.DropIndex(
                name: "ix_products_name",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_popularity",
                table: "products");
        }
    }
}
