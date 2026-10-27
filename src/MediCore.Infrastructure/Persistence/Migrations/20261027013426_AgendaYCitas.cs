using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgendaYCitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAtLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointments_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointments_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysMask = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doctor_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_doctor_schedules_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_DoctorId_StartsAtLocal",
                table: "appointments",
                columns: new[] { "DoctorId", "StartsAtLocal" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_idempotency",
                table: "appointments",
                columns: new[] { "CreatedByUserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_PatientId_StartsAtLocal",
                table: "appointments",
                columns: new[] { "PatientId", "StartsAtLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_doctor_schedules_DoctorId",
                table: "doctor_schedules",
                column: "DoctorId",
                unique: true);

            // PostgreSQL garantiza que dos citas ACTIVAS no se solapen, aun con solicitudes simultáneas:
            // el mismo médico no puede tener dos citas en el mismo intervalo, ni el mismo paciente estar en dos citas a la vez.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(@"
                ALTER TABLE appointments ADD CONSTRAINT ex_appointments_doctor_overlap
                EXCLUDE USING gist (""DoctorId"" WITH =, tsrange(""StartsAtLocal"", ""EndsAtLocal"", '[)') WITH &&)
                WHERE (""Status"" = 'Confirmed');");
            migrationBuilder.Sql(@"
                ALTER TABLE appointments ADD CONSTRAINT ex_appointments_patient_overlap
                EXCLUDE USING gist (""PatientId"" WITH =, tsrange(""StartsAtLocal"", ""EndsAtLocal"", '[)') WITH &&)
                WHERE (""Status"" = 'Confirmed');");
            migrationBuilder.Sql(@"
                ALTER TABLE appointments ADD CONSTRAINT ck_appointments_range CHECK (""EndsAtLocal"" > ""StartsAtLocal"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "doctor_schedules");
        }
    }
}
