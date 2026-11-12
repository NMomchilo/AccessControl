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
                name: "AccessPolicies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Entitlement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPolicies", x => x.Id);
                });

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
                    Entitlement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleEntitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EndpointPolicyEntitlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EndpointPolicyId = table.Column<long>(type: "bigint", nullable: false),
                    Entitlement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndpointPolicyEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndpointPolicyEntitlements_EndpointPolicies_EndpointPolicyId",
                        column: x => x.EndpointPolicyId,
                        principalTable: "EndpointPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AuthorizationRevisions",
                columns: new[] { "Id", "Version" },
                values: new object[] { 1, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_AccessPolicies_TenantId_Module_Resource_Action_Entitlement",
                table: "AccessPolicies",
                columns: new[] { "TenantId", "Module", "Resource", "Action", "Entitlement" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicies_TenantId_UnitOrganizationId_Module_HttpMethod_RouteTemplate",
                table: "EndpointPolicies",
                columns: new[] { "TenantId", "UnitOrganizationId", "Module", "HttpMethod", "RouteTemplate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndpointPolicyEntitlements_EndpointPolicyId_Entitlement",
                table: "EndpointPolicyEntitlements",
                columns: new[] { "EndpointPolicyId", "Entitlement" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleEntitlements_TenantId_UnitOrganizationId_RoleId_Entitlement",
                table: "RoleEntitlements",
                columns: new[] { "TenantId", "UnitOrganizationId", "RoleId", "Entitlement" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessPolicies");

            migrationBuilder.DropTable(
                name: "AuthorizationRevisions");

            migrationBuilder.DropTable(
                name: "EndpointPolicyEntitlements");

            migrationBuilder.DropTable(
                name: "RoleEntitlements");

            migrationBuilder.DropTable(
                name: "EndpointPolicies");
        }
    }
}
