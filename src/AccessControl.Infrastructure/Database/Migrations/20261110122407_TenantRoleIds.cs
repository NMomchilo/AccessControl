using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControl.Infrastructure.Database.Migrations;

public partial class TenantRoleIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dbo.RoleEntitlements ADD RoleId uniqueidentifier NULL;
            """);
        migrationBuilder.Sql("""
            UPDATE dbo.RoleEntitlements SET RoleId = CONVERT(uniqueidentifier,
                SUBSTRING(HASHBYTES('SHA2_256',
                    N'role-v1|' + LOWER(TenantId) + N'|' + LOWER(Role)), 1, 16));
            """);
        migrationBuilder.Sql("""
            ALTER TABLE dbo.RoleEntitlements ALTER COLUMN RoleId uniqueidentifier NOT NULL;
            DROP INDEX IX_RoleEntitlements_TenantId_Role_Entitlement ON dbo.RoleEntitlements;
            ALTER TABLE dbo.RoleEntitlements DROP COLUMN Role;
            CREATE UNIQUE INDEX IX_RoleEntitlements_TenantId_RoleId_Entitlement
                ON dbo.RoleEntitlements (TenantId, RoleId, Entitlement);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Tenant/role identity migration is one-way; restore a database backup to roll back.");
}
