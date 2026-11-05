using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_Email",
                table: "Contacts");

            migrationBuilder.AddColumn<int>(
                name: "SessionVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Companies",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            // Existing rows get their key, then duplicates made before the unique indexes existed are archived
            // (the oldest row wins), so the indexes can be created on a live database.
            migrationBuilder.Sql("UPDATE Companies SET NameKey = lower(trim(Name));");
            migrationBuilder.Sql("""
                UPDATE Companies SET IsDeleted = 1, DeletedAt = CURRENT_TIMESTAMP
                WHERE IsDeleted = 0 AND Id NOT IN (SELECT MIN(Id) FROM Companies WHERE IsDeleted = 0 GROUP BY NameKey);
                """);
            migrationBuilder.Sql("""
                UPDATE Contacts SET IsDeleted = 1, DeletedAt = CURRENT_TIMESTAMP
                WHERE IsDeleted = 0 AND Id NOT IN (SELECT MIN(Id) FROM Contacts WHERE IsDeleted = 0 GROUP BY lower(Email));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Email",
                table: "Contacts",
                column: "Email",
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_NameKey",
                table: "Companies",
                column: "NameKey",
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_At",
                table: "AuditLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_Email",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Companies_NameKey",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Email",
                table: "Contacts",
                column: "Email");
        }
    }
}
