using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSuccessors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurrencePredecessorTaskId",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceSuccessorTaskId",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringSuccessorOperations",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuccessorTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSuccessorOperations", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_RecurringSuccessorOperations_TaskItems_SourceTaskId",
                        column: x => x.SourceTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringSuccessorOperations_TaskItems_SuccessorTaskId",
                        column: x => x.SuccessorTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_RecurrencePredecessorTaskId",
                table: "TaskItems",
                column: "RecurrencePredecessorTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_RecurrenceSuccessorTaskId",
                table: "TaskItems",
                column: "RecurrenceSuccessorTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSuccessorOperations_SourceTaskId",
                table: "RecurringSuccessorOperations",
                column: "SourceTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSuccessorOperations_SuccessorTaskId",
                table: "RecurringSuccessorOperations",
                column: "SuccessorTaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringSuccessorOperations");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_RecurrencePredecessorTaskId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_RecurrenceSuccessorTaskId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "RecurrencePredecessorTaskId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "RecurrenceSuccessorTaskId",
                table: "TaskItems");
        }
    }
}
