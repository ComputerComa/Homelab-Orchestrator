using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomelabOrchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSshKeyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SshEnrollmentTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshEnrollmentTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SshPublicKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", nullable: false),
                    KeyDataBase64 = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshPublicKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SshEnrollmentTokens_TokenHash",
                table: "SshEnrollmentTokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_SshPublicKeys_Fingerprint",
                table: "SshPublicKeys",
                column: "Fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SshEnrollmentTokens");

            migrationBuilder.DropTable(
                name: "SshPublicKeys");
        }
    }
}
