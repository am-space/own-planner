using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskLists_TaskListId",
                table: "TaskItems");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveTaskListId",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrashedAt",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE TaskItems SET ActiveTaskListId = TaskListId;");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ActiveTaskListId",
                table: "TaskItems",
                column: "ActiveTaskListId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TrashedAt",
                table: "TaskItems",
                column: "TrashedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskLists_ActiveTaskListId",
                table: "TaskItems",
                column: "ActiveTaskListId",
                principalTable: "TaskLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskLists_ActiveTaskListId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ActiveTaskListId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_TrashedAt",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ActiveTaskListId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "TrashedAt",
                table: "TaskItems");

            migrationBuilder.Sql("DELETE FROM TaskItems WHERE TaskListId NOT IN (SELECT Id FROM TaskLists);");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskLists_TaskListId",
                table: "TaskItems",
                column: "TaskListId",
                principalTable: "TaskLists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
