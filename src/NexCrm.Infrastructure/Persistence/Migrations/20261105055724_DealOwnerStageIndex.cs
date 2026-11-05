using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealOwnerStageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Deals_OwnerId_Stage_ClosedAt",
                table: "Deals",
                columns: new[] { "OwnerId", "Stage", "ClosedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deals_OwnerId_Stage_ClosedAt",
                table: "Deals");
        }
    }
}
