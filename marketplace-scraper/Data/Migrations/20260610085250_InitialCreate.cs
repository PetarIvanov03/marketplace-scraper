using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace marketplace_scraper.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceBgn = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    PriceEur = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    IsNegotiable = table.Column<bool>(type: "bit", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsNew = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ListingsFound = table.Column<int>(type: "int", nullable: false),
                    NewListingsCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_ExternalId_Source",
                table: "Listings",
                columns: new[] { "ExternalId", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsActive",
                table: "Listings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsNew",
                table: "Listings",
                column: "IsNew");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_PublishedAt",
                table: "Listings",
                column: "PublishedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Source",
                table: "Listings",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRuns_Source",
                table: "ScrapeRuns",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRuns_StartedAt",
                table: "ScrapeRuns",
                column: "StartedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "ScrapeRuns");
        }
    }
}
