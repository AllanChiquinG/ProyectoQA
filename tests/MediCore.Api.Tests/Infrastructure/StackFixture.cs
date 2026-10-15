using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Contracts.Auth;
using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Domain.Patients;
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
