using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasEcommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1SellerCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    BrandName = table.Column<string>(type: "text", nullable: true),
                    ShortDescription = table.Column<string>(type: "text", nullable: true),
                    RichDescriptionHtml = table.Column<string>(type: "text", nullable: true),
                    HsnCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    GstPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    BulletPoints = table.Column<List<string>>(type: "jsonb", nullable: false),
                    SeoKeywords = table.Column<List<string>>(type: "jsonb", nullable: false),
                    APlusContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    HasVariants = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    IsAssuredQualityBadge = table.Column<bool>(type: "boolean", nullable: false),
                    ReplacementDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProductMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    AltText = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductMedia_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Size = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Material = table.Column<string>(type: "text", nullable: true),
                    PackSize = table.Column<string>(type: "text", nullable: true),
                    Mrp = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SalePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    SaleStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SaleEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StockQty = table.Column<int>(type: "integer", nullable: false),
                    BufferStockQty = table.Column<int>(type: "integer", nullable: false),
                    LowStockAlertThreshold = table.Column<int>(type: "integer", nullable: false),
                    WeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric", nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric", nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId",
                table: "Categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId_Slug",
                table: "Categories",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductId",
                table: "ProductMedia",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_TenantId",
                table: "ProductMedia",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_TenantId_ProductId_DisplayOrder",
                table: "ProductMedia",
                columns: new[] { "TenantId", "ProductId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Slug",
                table: "Products",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Status_CategoryId",
                table: "Products",
                columns: new[] { "TenantId", "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId",
                table: "ProductVariants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_TenantId_Sku",
                table: "ProductVariants",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.Sql(@"
    ALTER TABLE ""Categories"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""Categories"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""Categories"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));

    ALTER TABLE ""Products"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""Products"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""Products"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));

    ALTER TABLE ""ProductVariants"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""ProductVariants"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""ProductVariants"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));

    ALTER TABLE ""ProductMedia"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""ProductMedia"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""ProductMedia"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));
");    
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductMedia");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
