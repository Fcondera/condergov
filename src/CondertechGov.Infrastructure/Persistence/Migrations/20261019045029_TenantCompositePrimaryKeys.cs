using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondertechGov.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantCompositePrimaryKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Units_Units_TenantId_ParentId", table: "Units");
            migrationBuilder.DropUniqueConstraint(
                name: "AK_Units_TenantId_Id",
                table: "Units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Units",
                table: "Units");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Sessions_TenantId_Id",
                table: "Sessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Notifications_TenantId_Id",
                table: "Notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Licenses_TenantId_Id",
                table: "Licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Files_TenantId_Id",
                table: "Files");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Files",
                table: "Files");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Audit_TenantId_Id",
                table: "Audit");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Audit",
                table: "Audit");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Units",
                table: "Units",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Files",
                table: "Files",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Audit",
                table: "Audit",
                columns: new[] { "TenantId", "Id" });
            migrationBuilder.AddForeignKey(name: "FK_Units_Units_TenantId_ParentId", table: "Units",
                columns: new[] { "TenantId", "ParentId" }, principalTable: "Units",
                principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Units_Units_TenantId_ParentId", table: "Units");
            migrationBuilder.DropPrimaryKey(
                name: "PK_Units",
                table: "Units");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Files",
                table: "Files");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Audit",
                table: "Audit");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Units_TenantId_Id",
                table: "Units",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Units",
                table: "Units",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Sessions_TenantId_Id",
                table: "Sessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sessions",
                table: "Sessions",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Notifications_TenantId_Id",
                table: "Notifications",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Licenses_TenantId_Id",
                table: "Licenses",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Licenses",
                table: "Licenses",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Files_TenantId_Id",
                table: "Files",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Files",
                table: "Files",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Audit_TenantId_Id",
                table: "Audit",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Audit",
                table: "Audit",
                column: "Id");
            migrationBuilder.AddForeignKey(name: "FK_Units_Units_TenantId_ParentId", table: "Units",
                columns: new[] { "TenantId", "ParentId" }, principalTable: "Units",
                principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        }
    }
}
