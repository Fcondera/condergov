using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondertechGov.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotentCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestDigest",
                table: "Units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestKey",
                table: "Units",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningDigest",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProvisioningKey",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_TenantId_RequestKey",
                table: "Units",
                columns: new[] { "TenantId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ProvisioningKey",
                table: "Tenants",
                column: "ProvisioningKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Units_TenantId_RequestKey",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ProvisioningKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RequestDigest",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "ProvisioningDigest",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ProvisioningKey",
                table: "Tenants");
        }
    }
}
