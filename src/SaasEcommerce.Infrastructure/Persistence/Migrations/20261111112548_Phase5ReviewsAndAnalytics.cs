using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasEcommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ReviewsAndAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyerName = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsVerifiedPurchase = table.Column<bool>(type: "boolean", nullable: false),
                    MediaUrls = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    SellerReply = table.Column<string>(type: "text", nullable: true),
                    SellerRepliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId",
                table: "ProductReviews",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_TenantId",
                table: "ProductReviews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_TenantId_ProductId_BuyerId",
                table: "ProductReviews",
                columns: new[] { "TenantId", "ProductId", "BuyerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_TenantId_ProductId_IsApproved",
                table: "ProductReviews",
                columns: new[] { "TenantId", "ProductId", "IsApproved" });
            
            migrationBuilder.Sql(@"
    ALTER TABLE ""ProductReviews"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""ProductReviews"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""ProductReviews"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductReviews");
        }
    }
}
