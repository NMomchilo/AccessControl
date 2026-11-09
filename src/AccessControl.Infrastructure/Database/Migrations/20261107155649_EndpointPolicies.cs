using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControl.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class EndpointPolicies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EndpointPolicies",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
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

        migrationBuilder.CreateIndex(
            name: "IX_EndpointPolicies_TenantId_Module_HttpMethod_RouteTemplate",
            table: "EndpointPolicies",
            columns: new[] { "TenantId", "Module", "HttpMethod", "RouteTemplate" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EndpointPolicyEntitlements_EndpointPolicyId_Entitlement",
            table: "EndpointPolicyEntitlements",
            columns: new[] { "EndpointPolicyId", "Entitlement" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EndpointPolicyEntitlements");

        migrationBuilder.DropTable(
            name: "EndpointPolicies");
    }
}
