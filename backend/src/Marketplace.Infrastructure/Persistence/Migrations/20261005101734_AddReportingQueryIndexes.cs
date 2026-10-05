using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_orders_placedat",
                table: "orders",
                column: "PlacedAt");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_available",
                table: "inventory",
                columns: new[] { "AvailableQuantity", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_placedat",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_inventory_available",
                table: "inventory");
        }
    }
}
