using System.Net;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Contracts.Patients;
using MediCore.Domain.Identity;

namespace MediCore.Api.Tests;

/// <summary>Pacientes: CRUD, búsqueda, autorización por rol y por recurso (IDOR) y auditoría.</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class PatientsApiTests(StackFixture stack)
{
    private static PatientRequest NewRequest(string? identification = null, string email = "ana@example.com") =>
        new("Ana Prueba", identification ?? $"DPI-{Guid.NewGuid():N}"[..20], new DateOnly(1994, 5, 18), "5550 2841", email);

    private async Task<List<AuditEntryDto>> AuditAsync()
    {
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        return (await admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=500"))!;
    }

    [Fact]
    public async Task Recepcion_crea_consulta_y_actualiza_un_paciente_con_auditoria()
    {
        var (client, login) = await stack.LoginClientAsync(UserRole.Recepcion);
        var request = NewRequest();

        var created = await client.PostAsJsonAsync("/api/patients", request);
        var patient = (await created.Content.ReadFromJsonAsync<PatientDto>())!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/patients/{patient.Id}", created.Headers.Location!.AbsolutePath);

        var fetched = await client.GetFromJsonAsync<PatientDto>($"/api/patients/{patient.Id}");
        Assert.Equal(request.FullName, fetched!.FullName);

        var updated = await client.PutAsJsonAsync($"/api/patients/{patient.Id}", request with { FullName = "Ana Sofía Actualizada" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Ana Sofía Actualizada", (await client.GetFromJsonAsync<PatientDto>($"/api/patients/{patient.Id}"))!.FullName);

        var audit = await AuditAsync();
        foreach (var action in new[] { "patients.created", "patients.read", "patients.updated" })
            Assert.Contains(audit, e => e.Action == action && e.ActorUserId == login.User.Id && e.ResourceId == patient.Id.ToString());
        // La auditoría no guarda datos personales del paciente.
        Assert.DoesNotContain(audit, e => (e.Details ?? "").Contains("Ana"));
    }

    [Fact]
    public async Task Identificacion_duplicada_devuelve_409_aunque_cambien_mayusculas_o_espacios()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var id = $"dup-{Guid.NewGuid():N}"[..16];
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/patients", NewRequest(id))).StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/patients", NewRequest(" " + id.ToUpperInvariant().Insert(3, " ")));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Altas_simultaneas_con_la_misma_identificacion_crean_un_solo_paciente()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var id = $"race-{Guid.NewGuid():N}"[..16];

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/patients", NewRequest(id))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("", "5550 2841", "ana@example.com", "FullName")]
    [InlineData("Ana", "abc", "ana@example.com", "Phone")]
    [InlineData("Ana", "5550 2841", "no-es-correo", "Email")]
    public async Task Datos_invalidos_devuelven_400_con_el_campo_afectado(string name, string phone, string email, string field)
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var request = new PatientRequest(name, $"X-{Guid.NewGuid():N}"[..14], new DateOnly(1990, 1, 1), phone, email);

        var response = await client.PostAsJsonAsync("/api/patients", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fecha_de_nacimiento_futura_devuelve_400()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var request = NewRequest() with { BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)) };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/patients", request)).StatusCode);
    }

    [Fact]
    public async Task Busqueda_filtra_por_nombre_o_identificacion_y_pagina()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Administrador);
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
            await stack.CreatePatientAsync(name: $"Buscable {tag} {i}", identification: $"BUS-{tag}-{i}");

        var byName = await client.GetFromJsonAsync<PagedResult<PatientDto>>($"/api/patients?q={tag}&pageSize=2");
        var byIdentification = await client.GetFromJsonAsync<PagedResult<PatientDto>>($"/api/patients?q=BUS-{tag}-1");
        var literalPercent = await client.GetFromJsonAsync<PagedResult<PatientDto>>("/api/patients?q=%25");

        Assert.Equal(3, byName!.Total);
        Assert.Equal(2, byName.Items.Count);
        Assert.Single(byIdentification!.Items);
        // El comodín % se trata como texto, no como "todos".
        Assert.DoesNotContain(literalPercent!.Items, p => p.FullName.StartsWith("Buscable"));
    }

    [Fact]
    public async Task Paciente_lee_su_propio_registro_pero_no_el_de_otro_y_el_intento_queda_auditado()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var own = await stack.CreatePatientAsync(userId);
        var other = await stack.CreatePatientAsync();
        var (client, login) = await stack.LoginClientForAsync(email);

        var mine = await client.GetFromJsonAsync<PatientDto>("/api/patients/me");
        var byId = await client.GetAsync($"/api/patients/{own.Id}");
        var othersRecord = await client.GetAsync($"/api/patients/{other.Id}");

        Assert.Equal(own.Id, mine!.Id);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, othersRecord.StatusCode);
        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "patients.access_denied" && e.Outcome == "Failure"
            && e.ActorUserId == login.User.Id && e.ResourceId == other.Id.ToString());
    }

    [Fact]
    public async Task Paciente_no_puede_listar_crear_ni_modificar_pacientes()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var own = await stack.CreatePatientAsync(userId);
        var (client, _) = await stack.LoginClientForAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/patients", NewRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/patients/{own.Id}", NewRequest())).StatusCode);
    }

    [Fact]
    public async Task Medico_sin_relacion_de_atencion_no_ve_pacientes()
    {
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Medico);

        var list = await client.GetFromJsonAsync<PagedResult<PatientDto>>("/api/patients");

        Assert.Empty(list!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{patient.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/patients", NewRequest())).StatusCode);
    }

    [Fact]
    public async Task Laboratorio_no_tiene_acceso_a_pacientes()
    {
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Laboratorio);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/patients/{patient.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sin_token_no_se_accede_y_un_id_inexistente_devuelve_404()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await stack.CreateClient().GetAsync($"/api/patients/{Guid.NewGuid()}")).StatusCode);

        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/patients/{Guid.NewGuid()}", NewRequest())).StatusCode);
    }

    [Fact]
    public async Task Actualizar_a_una_identificacion_de_otro_paciente_devuelve_409()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var first = await stack.CreatePatientAsync();
        var second = await stack.CreatePatientAsync();

        var response = await client.PutAsJsonAsync($"/api/patients/{second.Id}", NewRequest(first.IdentificationNumber));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
