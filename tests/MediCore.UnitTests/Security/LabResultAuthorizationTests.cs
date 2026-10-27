using System.Security.Claims;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Domain.Laboratory;
using MediCore.Domain.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.UnitTests.Security;

/// <summary>Autorización por recurso de los resultados de laboratorio, sin base de datos.</summary>
public class LabResultAuthorizationTests
{
    private sealed class FakeCareRelationship(bool result) : ICareRelationship
    {
        public int Calls { get; private set; }

        public Task<bool> ExistsAsync(Guid doctorUserId, Guid patientId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private readonly Guid _patientUser = Guid.NewGuid();

    private static ClaimsPrincipal User(string? role, Guid? userId = null)
    {
        var claims = new List<Claim>();
        if (role is not null) claims.Add(new Claim(ClaimNames.Role, role));
        if (userId is not null) claims.Add(new Claim(ClaimNames.Subject, userId.ToString()!));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimNames.Name, ClaimNames.Role));
    }

    private LabResult NewResult() => new()
    {
        ExamType = "Hematología",
        Patient = new Patient { FullName = "P", IdentificationNumber = "1", NormalizedIdentification = "1", Phone = "1", Email = "p@example.com", UserId = _patientUser },
    };

    private static async Task<(bool Allowed, int RelationChecks)> CheckAsync(ClaimsPrincipal user, LabResult result, bool relation = false)
    {
        var fake = new FakeCareRelationship(relation);
        var handler = new LabResultAuthorizationHandler(new ServiceCollection().AddSingleton<ICareRelationship>(fake).BuildServiceProvider());
        var context = new AuthorizationHandlerContext([LabResultOperations.Read], user, result);
        await handler.HandleAsync(context);
        return (context.HasSucceeded, fake.Calls);
    }

    [Fact]
    public async Task Laboratorio_lee_cualquier_resultado_sin_consultar_la_relacion_de_atencion()
    {
        var (allowed, checks) = await CheckAsync(User(Roles.Laboratorio, Guid.NewGuid()), NewResult());

        Assert.True(allowed);
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task Paciente_lee_solo_su_propio_resultado()
    {
        var result = NewResult();

        Assert.True((await CheckAsync(User(Roles.Paciente, _patientUser), result)).Allowed);
        Assert.False((await CheckAsync(User(Roles.Paciente, Guid.NewGuid()), result)).Allowed);
    }

    [Fact]
    public async Task Medico_lee_el_resultado_solo_con_relacion_valida_de_atencion()
    {
        var result = NewResult();
        var doctor = User(Roles.Medico, Guid.NewGuid());

        Assert.True((await CheckAsync(doctor, result, relation: true)).Allowed);
        var denied = await CheckAsync(doctor, result, relation: false);
        Assert.False(denied.Allowed);
        Assert.Equal(1, denied.RelationChecks);
    }

    [Theory]
    [InlineData(Roles.Recepcion)]
    [InlineData(Roles.Administrador)]
    public async Task Recepcion_y_administrador_nunca_leen_resultados_aunque_exista_relacion(string role)
    {
        var (allowed, checks) = await CheckAsync(User(role, Guid.NewGuid()), NewResult(), relation: true);

        Assert.False(allowed);
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task Sin_identificador_de_usuario_o_sin_rol_no_hay_acceso()
    {
        Assert.False((await CheckAsync(User(Roles.Laboratorio), NewResult())).Allowed);
        Assert.False((await CheckAsync(User(null, Guid.NewGuid()), NewResult())).Allowed);
    }
}
