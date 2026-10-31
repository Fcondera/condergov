using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondertechGov.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindRefreshSessionSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Sessions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Sessions");
        }
    }
}
