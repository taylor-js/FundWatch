using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FundWatch.Data.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class RemovedForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppStockSimulations_AspNetUsers_UserId",
                table: "AppStockSimulations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppStockTransactions_AspNetUsers_UserId",
                table: "AppStockTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_AppUserStocks_AspNetUsers_UserId",
                table: "AppUserStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_AppWatchlists_AspNetUsers_UserId",
                table: "AppWatchlists");

            migrationBuilder.DropIndex(
                name: "IX_AppWatchlists_UserId",
                table: "AppWatchlists");

            migrationBuilder.DropIndex(
                name: "IX_AppUserStocks_UserId",
                table: "AppUserStocks");

            migrationBuilder.DropIndex(
                name: "IX_AppStockTransactions_UserId",
                table: "AppStockTransactions");

            migrationBuilder.DropIndex(
                name: "IX_AppStockSimulations_UserId",
                table: "AppStockSimulations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppWatchlists_UserId",
                table: "AppWatchlists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUserStocks_UserId",
                table: "AppUserStocks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStockTransactions_UserId",
                table: "AppStockTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStockSimulations_UserId",
                table: "AppStockSimulations",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppStockSimulations_AspNetUsers_UserId",
                table: "AppStockSimulations",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AppStockTransactions_AspNetUsers_UserId",
                table: "AppStockTransactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AppUserStocks_AspNetUsers_UserId",
                table: "AppUserStocks",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AppWatchlists_AspNetUsers_UserId",
                table: "AppWatchlists",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
