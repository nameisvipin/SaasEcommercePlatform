using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using SaasEcommerce.Core.Entities;

#nullable disable

namespace SaasEcommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4LogisticsAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RtoDisputeClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwbNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IssueType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SellerRemarks = table.Column<string>(type: "text", nullable: false),
                    ProofMediaUrls = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AdminResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RtoDisputeClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    CourierName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AwbNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExternalShipmentId = table.Column<string>(type: "text", nullable: true),
                    ChargeableWeightGrams = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric", nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric", nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric", nullable: false),
                    EstimatedFreightCost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LiveTrackingUrl = table.Column<string>(type: "text", nullable: true),
                    Checkpoints = table.Column<List<TrackingCheckpoint>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    PickedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RtoDisputeClaims_TenantId",
                table: "RtoDisputeClaims",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RtoDisputeClaims_TenantId_ShipmentId",
                table: "RtoDisputeClaims",
                columns: new[] { "TenantId", "ShipmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId",
                table: "Shipments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_AwbNumber",
                table: "Shipments",
                columns: new[] { "TenantId", "AwbNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_OrderId",
                table: "Shipments",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);
            
            migrationBuilder.Sql(@"
    ALTER TABLE ""Shipments"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""Shipments"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""Shipments"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));

    ALTER TABLE ""RtoDisputeClaims"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""RtoDisputeClaims"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation_policy ON ""RtoDisputeClaims"" AS RESTRICTIVE FOR ALL
        USING (current_setting('app.current_tenant', true) = 'SUPERADMIN' OR ""TenantId""::text = current_setting('app.current_tenant', true));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RtoDisputeClaims");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
