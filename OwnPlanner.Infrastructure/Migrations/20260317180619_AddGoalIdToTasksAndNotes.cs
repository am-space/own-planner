using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalIdToTasksAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "NoteItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_GoalId",
                table: "TaskItems",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteItems_GoalId",
                table: "NoteItems",
                column: "GoalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_GoalId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_NoteItems_GoalId",
                table: "NoteItems");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "NoteItems");
        }
    }
}
