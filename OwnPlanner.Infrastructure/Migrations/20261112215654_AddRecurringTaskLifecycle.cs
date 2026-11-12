using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTaskLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Recurrence",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceCalendarKey",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceCheckedBefore",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RecurrenceRevision",
                table: "TaskItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "TaskOccurrenceFacts",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskOccurrenceFacts", x => new { x.TaskId, x.OccurrenceDate });
                    table.CheckConstraint("CK_TaskOccurrenceFacts_Status_CompletedAt", "(Status = 0 AND CompletedAt IS NOT NULL) OR (Status IN (1, 2) AND CompletedAt IS NULL)");
                    table.ForeignKey(
                        name: "FK_TaskOccurrenceFacts_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskOccurrenceFacts_CompletedAt",
                table: "TaskOccurrenceFacts",
                column: "CompletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskOccurrenceFacts");

            migrationBuilder.DropColumn(
                name: "Recurrence",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "RecurrenceCalendarKey",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "RecurrenceCheckedBefore",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "RecurrenceRevision",
                table: "TaskItems");
        }
    }
}
