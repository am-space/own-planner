using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPausedGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ActiveGoalLimitWarned",
                table: "WeeklyReviews",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PausedGoalsMentionMonth",
                table: "WeeklyReviewPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResumedAt",
                table: "Goals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "Goals",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveGoalLimitWarned",
                table: "WeeklyReviews");

            migrationBuilder.DropColumn(
                name: "PausedGoalsMentionMonth",
                table: "WeeklyReviewPreferences");

            migrationBuilder.DropColumn(
                name: "LastResumedAt",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "Goals");
        }
    }
}
