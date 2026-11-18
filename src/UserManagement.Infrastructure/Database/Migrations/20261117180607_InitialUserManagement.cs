using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagement.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialUserManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitOrganizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOrganizations", x => x.Id);
                    table.UniqueConstraint("AK_UnitOrganizations_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "UserManagementRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserManagementRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true, computedColumnSql: "LOWER([Email])", stored: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsSuperAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.UniqueConstraint("AK_Users_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.UniqueConstraint("AK_Roles_TenantId_UnitOrganizationId_Id", x => new { x.TenantId, x.UnitOrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Roles_UnitOrganizations_TenantId_UnitOrganizationId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId },
                        principalTable: "UnitOrganizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitOrganizationMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOrganizationMemberships", x => x.Id);
                    table.UniqueConstraint("AK_UnitOrganizationMemberships_TenantId_UserId_UnitOrganizationId", x => new { x.TenantId, x.UserId, x.UnitOrganizationId });
                    table.ForeignKey(
                        name: "FK_UnitOrganizationMemberships_UnitOrganizations_TenantId_UnitOrganizationId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId },
                        principalTable: "UnitOrganizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitOrganizationMemberships_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_TenantId_UnitOrganizationId_RoleId",
                        columns: x => new { x.TenantId, x.UnitOrganizationId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "TenantId", "UnitOrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_UnitOrganizationMemberships_TenantId_UserId_UnitOrganizationId",
                        columns: x => new { x.TenantId, x.UserId, x.UnitOrganizationId },
                        principalTable: "UnitOrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId", "UnitOrganizationId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.InsertData(
                table: "UserManagementRevisions",
                columns: new[] { "Id", "LastUpdateTime", "LastUpdateUser", "Version" },
                values: new object[] { new Guid("a9acd89c-9a57-48c3-9dd1-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Migration", 0L });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_UnitOrganizationId_Code",
                table: "Roles",
                columns: new[] { "TenantId", "UnitOrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrganizationMemberships_TenantId_UnitOrganizationId",
                table: "UnitOrganizationMemberships",
                columns: new[] { "TenantId", "UnitOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrganizationMemberships_UserId_UnitOrganizationId",
                table: "UnitOrganizationMemberships",
                columns: new[] { "UserId", "UnitOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrganizations_TenantId_Name",
                table: "UnitOrganizations",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_TenantId_UnitOrganizationId_RoleId",
                table: "UserRoles",
                columns: new[] { "TenantId", "UnitOrganizationId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_TenantId_UserId_UnitOrganizationId",
                table: "UserRoles",
                columns: new[] { "TenantId", "UserId", "UnitOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_UnitOrganizationId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "UnitOrganizationId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_NormalizedEmail",
                table: "Users",
                columns: new[] { "TenantId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_ObjectId",
                table: "Users",
                columns: new[] { "TenantId", "ObjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserManagementRevisions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "UnitOrganizationMemberships");

            migrationBuilder.DropTable(
                name: "UnitOrganizations");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
