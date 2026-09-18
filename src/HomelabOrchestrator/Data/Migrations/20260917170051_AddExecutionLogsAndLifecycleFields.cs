using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomelabOrchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionLogsAndLifecycleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Output",
                table: "AnsibleExecutions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "ResolvedTargetHostnamesJson",
                table: "AnsibleExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "AnsibleExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "AnsibleExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnsibleExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Stream = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnsibleExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnsibleExecutionLogs_ExecutionId_Sequence",
                table: "AnsibleExecutionLogs",
                columns: new[] { "ExecutionId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnsibleExecutionLogs");

            migrationBuilder.DropColumn(
                name: "ResolvedTargetHostnamesJson",
                table: "AnsibleExecutions");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "AnsibleExecutions");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "AnsibleExecutions");

            migrationBuilder.AlterColumn<string>(
                name: "Output",
                table: "AnsibleExecutions",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
