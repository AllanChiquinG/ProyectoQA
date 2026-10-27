using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Contracts.Auth;
using MediCore.Domain.Clinical;
using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace MediCore.Api.Tests.Infrastructure;

/// <summary>API real contra PostgreSQL y Redis desechables en Docker, con migraciones aplicadas.</summary>
public sealed class StackFixture : ApiFactory, IAsyncLifetime
{
    public const string Password = "Prueba-Segura-123!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MediCoreDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
    }

    /// <summary>Crea un usuario nuevo y único (para que las pruebas no se afecten entre sí) y devuelve su correo.</summary>
    public async Task<(Guid Id, string Email)> CreateUserAsync(UserRole role, bool active = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var email = $"{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@example.com";
        var user = new User { Email = email, NormalizedEmail = email, DisplayName = $"Usuario {role}", Role = role, IsActive = active, PasswordHash = "" };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    public async Task<Patient> CreatePatientAsync(Guid? userId = null, string? identification = null, string name = "Paciente de Prueba")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        identification ??= $"ID-{Guid.NewGuid():N}"[..20];
        var patient = new Patient
        {
            FullName = name,
            IdentificationNumber = identification,
            NormalizedIdentification = Patient.NormalizeIdentification(identification),
            BirthDate = new DateOnly(1990, 1, 1),
            Phone = "5550 0000",
            Email = "paciente@example.com",
            UserId = userId,
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient;
    }

    public async Task<Doctor> CreateDoctorAsync(string name = "Dr. Prueba", string specialty = "Cardiología")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var doctor = new Doctor { FullName = name, Specialty = specialty, Phone = "5550 1111", Email = "doctor@example.com" };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return doctor;
    }

    /// <summary>Médico que atiende todos los días de 08:00 a 12:00 (8 horarios), para que las pruebas no dependan del día de la semana.</summary>
    public async Task<Doctor> CreateScheduledDoctorAsync(Guid? userId = null, string name = "Dr. Agenda", string start = "08:00", string end = "12:00")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var doctor = new Doctor { FullName = $"{name} {Guid.NewGuid():N}"[..Math.Min(40, name.Length + 9)], Specialty = "Medicina general", Phone = "5550 1111", Email = "agenda@example.com", UserId = userId };
        db.Doctors.Add(doctor);
        db.DoctorSchedules.Add(new DoctorSchedule
        {
            DoctorId = doctor.Id,
            DaysMask = DoctorSchedule.ToMask(Enum.GetValues<DayOfWeek>()),
            StartTime = TimeOnly.Parse(start),
            EndTime = TimeOnly.Parse(end),
        });
        await db.SaveChangesAsync();
        return doctor;
    }

    public async Task<Appointment> CreateAppointmentAsync(Guid patientId, Guid doctorId, DateTime startsAtLocal, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var appointment = new Appointment
        {
            PatientId = patientId, DoctorId = doctorId,
            StartsAtLocal = DateTime.SpecifyKind(startsAtLocal, DateTimeKind.Unspecified),
            EndsAtLocal = DateTime.SpecifyKind(startsAtLocal.AddMinutes(30), DateTimeKind.Unspecified),
            Status = status, CreatedByUserId = Guid.NewGuid(),
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    /// <summary>Consulta insertada directamente en la base de datos (para preparar estados que la API no permite crear).</summary>
    public async Task<Consultation> CreateConsultationAsync(Appointment appointment, string reason = "Motivo de prueba")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var consultation = new Consultation
        {
            PatientId = appointment.PatientId, DoctorId = appointment.DoctorId, AppointmentId = appointment.Id,
            Reason = reason, Notes = "Notas de prueba", Diagnosis = "Diagnóstico de prueba",
        };
        db.Consultations.Add(consultation);
        await db.SaveChangesAsync();
        return consultation;
    }

    public async Task SetAppointmentStatusAsync(Guid appointmentId, AppointmentStatus status)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        await db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    /// <summary>Hora de la clínica (misma zona que la API en pruebas).</summary>
    public static DateTime ClinicNow => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Guatemala"), DateTimeKind.Unspecified);

    /// <summary>Fecha futura (días desde hoy, hora de la clínica = UTC-6, por eso se suman al menos 2 para no rozar el cambio de día).</summary>
    public static DateOnly Future(int days = 3) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Math.Max(days, 2));

    public async Task<(HttpClient Client, LoginResponse Login)> LoginClientForAsync(string email)
    {
        var client = CreateClient();
        var login = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login);
    }

    public static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public async Task<(HttpClient Client, LoginResponse Login)> LoginClientAsync(UserRole role)
    {
        var (_, email) = await CreateUserAsync(role);
        var client = CreateClient();
        var login = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class StackCollection : ICollectionFixture<StackFixture>
{
    public const string Name = "Stack";
}
