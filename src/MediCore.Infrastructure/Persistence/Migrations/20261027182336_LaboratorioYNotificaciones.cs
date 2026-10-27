using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaboratorioYNotificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "lab_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lab_results_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lab_result_parameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LabResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Value = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReferenceRange = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_result_parameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lab_result_parameters_lab_results_LabResultId",
                        column: x => x.LabResultId,
                        principalTable: "lab_results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                column: "NextAttemptAtUtc",
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lab_result_parameters_LabResultId_Position",
                table: "lab_result_parameters",
                columns: new[] { "LabResultId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_results_CreatedAtUtc",
                table: "lab_results",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_lab_results_PatientId_PerformedOn",
                table: "lab_results",
                columns: new[] { "PatientId", "PerformedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_EventId",
                table: "notifications",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PatientId_CreatedAtUtc",
                table: "notifications",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_Type_SourceId",
                table: "notifications",
                columns: new[] { "Type", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_result_parameters");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "lab_results");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                column: "OccurredAtUtc",
                filter: "\"ProcessedAtUtc\" IS NULL");
        }
    }
}
