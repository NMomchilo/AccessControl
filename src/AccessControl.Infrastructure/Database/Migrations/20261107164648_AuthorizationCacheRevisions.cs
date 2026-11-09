using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessControl.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AuthorizationCacheRevisions : Migration
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

        migrationBuilder.InsertData(
            table: "AuthorizationRevisions",
            columns: new[] { "Id", "Version" },
            values: new object[] { 1, 0L });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuthorizationRevisions");
    }
}
