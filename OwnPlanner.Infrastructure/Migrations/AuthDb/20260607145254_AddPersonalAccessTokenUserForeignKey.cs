using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations.AuthDb
{
    /// <inheritdoc />
    public partial class AddPersonalAccessTokenUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_PersonalAccessTokens_Users_UserId",
                table: "PersonalAccessTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PersonalAccessTokens_Users_UserId",
                table: "PersonalAccessTokens");
        }
    }
}
