using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControl.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorizationRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EndpointPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RouteTemplate = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndpointPolicies", x => x.Id);
                    table.UniqueConstraint("AK_EndpointPolicies_TenantId_UnitOrganizationId_Id", x => new { x.TenantId, x.UnitOrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Entitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.Id);
                    table.UniqueConstraint("AK_Entitlements_TenantId_UnitOrganizationId_Id", x => new { x.TenantId, x.UnitOrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "EndpointPolicyEntitlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EndpointPolicyId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndpointPolicyEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndpointPolicyEntitlements_EndpointPolicies_TenantId_UnitOrganizationId_EndpointPolicyId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId, x.EndpointPolicyId },
                        principalTable: "EndpointPolicies",
                        principalColumns: new[] { "TenantId", "UnitOrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EndpointPolicyEntitlements_Entitlements_TenantId_UnitOrganizationId_EntitlementId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId, x.EntitlementId },
                        principalTable: "Entitlements",
                        principalColumns: new[] { "TenantId", "UnitOrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleEntitlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleEntitlements_Entitlements_TenantId_UnitOrganizationId_EntitlementId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId, x.EntitlementId },
                        principalTable: "Entitlements",
                        principalColumns: new[] { "TenantId", "UnitOrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AuthorizationRevisions",
                columns: new[] { "Id", "Version" },
                values: new object[] { 1, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicies_TenantId_UnitOrganizationId_Module_HttpMethod_RouteTemplate",
                table: "EndpointPolicies",
                columns: new[] { "TenantId", "UnitOrganizationId", "Module", "HttpMethod", "RouteTemplate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicyEntitlements_EndpointPolicyId_EntitlementId",
                table: "EndpointPolicyEntitlements",
                columns: new[] { "EndpointPolicyId", "EntitlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicyEntitlements_TenantId_UnitOrganizationId_EndpointPolicyId",
                table: "EndpointPolicyEntitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "EndpointPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicyEntitlements_TenantId_UnitOrganizationId_EntitlementId",
                table: "EndpointPolicyEntitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "EntitlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_TenantId_UnitOrganizationId_Code",
                table: "Entitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleEntitlements_TenantId_UnitOrganizationId_EntitlementId",
                table: "RoleEntitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "EntitlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleEntitlements_TenantId_UnitOrganizationId_RoleId_EntitlementId",
                table: "RoleEntitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "RoleId", "EntitlementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizationRevisions");

            migrationBuilder.DropTable(
                name: "EndpointPolicyEntitlements");

            migrationBuilder.DropTable(
                name: "RoleEntitlements");

            migrationBuilder.DropTable(
                name: "EndpointPolicies");

            migrationBuilder.DropTable(
                name: "Entitlements");
        }
    }
}
