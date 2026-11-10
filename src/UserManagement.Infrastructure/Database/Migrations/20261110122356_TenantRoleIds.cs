using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagement.Infrastructure.Database.Migrations;

public partial class TenantRoleIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dbo.UserRoles DROP CONSTRAINT FK_UserRoles_Users_UserId;
            DROP INDEX IX_Users_ObjectId ON dbo.Users;

            ALTER TABLE dbo.Users ADD TenantId nvarchar(128) NOT NULL
                CONSTRAINT DF_Users_TenantRoleIds_Tenant DEFAULT (N'default');
            ALTER TABLE dbo.Users DROP CONSTRAINT DF_Users_TenantRoleIds_Tenant;

            CREATE TABLE dbo.Roles (
                Id uniqueidentifier NOT NULL,
                TenantId nvarchar(128) NOT NULL,
                Code nvarchar(64) NOT NULL,
                DisplayName nvarchar(200) NOT NULL,
                CONSTRAINT PK_Roles PRIMARY KEY (Id),
                CONSTRAINT AK_Roles_TenantId_Id UNIQUE (TenantId, Id)
            );
            CREATE UNIQUE INDEX IX_Roles_TenantId_Code ON dbo.Roles (TenantId, Code);

            ALTER TABLE dbo.UserRoles ADD TenantId nvarchar(128) NULL, RoleId uniqueidentifier NULL;
            """);

        // SQL Server compiles column references before executing a batch. Backfill after the ADD batch.
        migrationBuilder.Sql("""
            UPDATE ur SET TenantId = u.TenantId,
                RoleId = CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256',
                    N'role-v1|' + LOWER(u.TenantId) + N'|' + LOWER(ur.Role)), 1, 16))
            FROM dbo.UserRoles ur INNER JOIN dbo.Users u ON u.Id = ur.UserId;

            INSERT INTO dbo.Roles (Id, TenantId, Code, DisplayName)
            SELECT RoleId, TenantId, LOWER(MIN(Role)), MIN(Role)
            FROM dbo.UserRoles GROUP BY RoleId, TenantId;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE dbo.UserRoles ALTER COLUMN TenantId nvarchar(128) NOT NULL;
            ALTER TABLE dbo.UserRoles ALTER COLUMN RoleId uniqueidentifier NOT NULL;
            ALTER TABLE dbo.UserRoles DROP CONSTRAINT PK_UserRoles;
            ALTER TABLE dbo.UserRoles DROP COLUMN Role;
            ALTER TABLE dbo.UserRoles ADD CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId);

            ALTER TABLE dbo.Users ADD CONSTRAINT AK_Users_TenantId_Id UNIQUE (TenantId, Id);
            CREATE UNIQUE INDEX IX_Users_TenantId_ObjectId ON dbo.Users (TenantId, ObjectId);
            CREATE INDEX IX_UserRoles_TenantId_RoleId ON dbo.UserRoles (TenantId, RoleId);
            CREATE INDEX IX_UserRoles_TenantId_UserId ON dbo.UserRoles (TenantId, UserId);
            ALTER TABLE dbo.UserRoles ADD CONSTRAINT FK_UserRoles_Roles_TenantId_RoleId
                FOREIGN KEY (TenantId, RoleId) REFERENCES dbo.Roles (TenantId, Id);
            ALTER TABLE dbo.UserRoles ADD CONSTRAINT FK_UserRoles_Users_TenantId_UserId
                FOREIGN KEY (TenantId, UserId) REFERENCES dbo.Users (TenantId, Id) ON DELETE CASCADE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Tenant/role identity migration is one-way; restore a database backup to roll back.");
}
