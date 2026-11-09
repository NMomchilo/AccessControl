using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControl.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
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
            name: "RoleEntitlements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Role = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Entitlement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoleEntitlements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccessPolicies_TenantId_Module_Resource_Action_Entitlement",
            table: "AccessPolicies",
            columns: new[] { "TenantId", "Module", "Resource", "Action", "Entitlement" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RoleEntitlements_TenantId_Role_Entitlement",
            table: "RoleEntitlements",
            columns: new[] { "TenantId", "Role", "Entitlement" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AccessPolicies");

        migrationBuilder.DropTable(
            name: "RoleEntitlements");
    }
}
