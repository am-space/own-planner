using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContextIdToLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContextId",
                table: "TaskLists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContextId",
                table: "NoteLists",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskLists_ContextId",
                table: "TaskLists",
                column: "ContextId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteLists_ContextId",
                table: "NoteLists",
                column: "ContextId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskLists_ContextId",
                table: "TaskLists");

            migrationBuilder.DropIndex(
                name: "IX_NoteLists_ContextId",
                table: "NoteLists");

            migrationBuilder.DropColumn(
                name: "ContextId",
                table: "TaskLists");

            migrationBuilder.DropColumn(
                name: "ContextId",
                table: "NoteLists");
        }
    }
}
