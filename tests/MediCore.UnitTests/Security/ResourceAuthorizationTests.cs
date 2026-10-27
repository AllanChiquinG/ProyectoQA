using System.Security.Claims;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Domain.Clinical;
using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.UnitTests.Security;

/// <summary>Reglas de autorización por recurso (¿puede ESTE usuario con ESTE rol actuar sobre ESTE registro?) sin base de datos.</summary>
public class ResourceAuthorizationTests
{
    /// <summary>Relación de atención simulada; cuenta cuántas veces se consultó (solo debe consultarse para médicos que no son autores).</summary>
    private sealed class FakeCareRelationship(bool result) : ICareRelationship
    {
        public int Calls { get; private set; }

        public Task<bool> ExistsAsync(Guid doctorUserId, Guid patientId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static IServiceProvider Services(ICareRelationship relationship)
        => new ServiceCollection().AddSingleton(relationship).BuildServiceProvider();

    private static ClaimsPrincipal User(string? role, Guid? userId = null)
    {
        var claims = new List<Claim>();
        if (role is not null) claims.Add(new Claim(ClaimNames.Role, role));
        if (userId is not null) claims.Add(new Claim(ClaimNames.Subject, userId.ToString()!));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimNames.Name, ClaimNames.Role));
    }

    private static async Task<bool> AllowedAsync<T>(AuthorizationHandler<OperationAuthorizationRequirement, T> handler, ClaimsPrincipal user, OperationAuthorizationRequirement operation, T resource)
    {
        var context = new AuthorizationHandlerContext([operation], user, resource);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private readonly Guid _doctorUser = Guid.NewGuid();
    private readonly Guid _patientUser = Guid.NewGuid();

    private Patient NewPatient() => new() { FullName = "P", IdentificationNumber = "1", NormalizedIdentification = "1", Phone = "1", Email = "p@example.com", UserId = _patientUser };

    private Doctor NewDoctor() => new() { FullName = "D", Specialty = "S", Phone = "1", Email = "d@example.com", UserId = _doctorUser };

    private Prescription NewPrescription() => new() { Doctor = NewDoctor(), Patient = NewPatient() };

    private Appointment NewAppointment() => new() { Doctor = NewDoctor(), Patient = NewPatient() };

    // ---- Recetas ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Receta_la_lee_su_autor_sin_consultar_la_relacion_y_su_paciente()
    {
        var relation = new FakeCareRelationship(false);
        var handler = new PrescriptionAuthorizationHandler(Services(relation));
        var prescription = NewPrescription();

        Assert.True(await AllowedAsync(handler, User(Roles.Medico, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.True(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), PrescriptionOperations.Read, prescription));
        Assert.Equal(0, relation.Calls); // ni el autor ni el paciente necesitan la relación de atención
    }

    [Fact]
    public async Task Receta_expediente_compartido_otro_medico_la_lee_solo_si_tiene_relacion_de_atencion()
    {
        var prescription = NewPrescription();
        var otherDoctor = User(Roles.Medico, Guid.NewGuid());

        Assert.True(await AllowedAsync(new PrescriptionAuthorizationHandler(Services(new FakeCareRelationship(true))), otherDoctor, PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(new PrescriptionAuthorizationHandler(Services(new FakeCareRelationship(false))), otherDoctor, PrescriptionOperations.Read, prescription));
    }

    [Theory]
    [InlineData(Roles.Recepcion)]
    [InlineData(Roles.Administrador)]
    [InlineData(Roles.Laboratorio)]
    public async Task Receta_no_la_leen_recepcion_administrador_ni_laboratorio_aunque_haya_relacion_o_sea_el_mismo_usuario(string role)
    {
        var relation = new FakeCareRelationship(true);
        var handler = new PrescriptionAuthorizationHandler(Services(relation));
        var prescription = NewPrescription();

        Assert.False(await AllowedAsync(handler, User(role, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(role, _patientUser), PrescriptionOperations.Read, prescription));
        Assert.Equal(0, relation.Calls);
    }

    [Fact]
    public async Task Receta_no_la_lee_otro_paciente_ni_un_rol_cruzado_ni_un_usuario_sin_identificador_o_sin_rol()
    {
        var handler = new PrescriptionAuthorizationHandler(Services(new FakeCareRelationship(false)));
        var prescription = NewPrescription();

        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, Guid.NewGuid()), PrescriptionOperations.Read, prescription));
        // El identificador del médico presentado con rol de paciente (y al revés) no sirve.
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(Roles.Medico, _patientUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(Roles.Medico), PrescriptionOperations.Read, prescription)); // sin identificador
        Assert.False(await AllowedAsync(handler, User(null, _doctorUser), PrescriptionOperations.Read, prescription)); // sin rol
    }

    // ---- Pacientes --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Paciente_lo_lee_recepcion_y_administrador_lo_gestionan_laboratorio_no()
    {
        var handler = new PatientAuthorizationHandler(Services(new FakeCareRelationship(false)));
        var patient = NewPatient();

        Assert.True(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), PatientOperations.Read, patient));
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), PatientOperations.Update, patient)); // no edita su ficha
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, Guid.NewGuid()), PatientOperations.Read, patient));
        foreach (var role in new[] { Roles.Recepcion, Roles.Administrador })
        {
            Assert.True(await AllowedAsync(handler, User(role, Guid.NewGuid()), PatientOperations.Read, patient));
            Assert.True(await AllowedAsync(handler, User(role, Guid.NewGuid()), PatientOperations.Update, patient));
        }

        Assert.False(await AllowedAsync(handler, User(Roles.Laboratorio, Guid.NewGuid()), PatientOperations.Read, patient));
    }

    [Fact]
    public async Task Paciente_medico_lo_lee_solo_con_relacion_de_atencion_y_nunca_lo_modifica()
    {
        var patient = NewPatient();
        var doctor = User(Roles.Medico, _doctorUser);

        Assert.True(await AllowedAsync(new PatientAuthorizationHandler(Services(new FakeCareRelationship(true))), doctor, PatientOperations.Read, patient));
        Assert.False(await AllowedAsync(new PatientAuthorizationHandler(Services(new FakeCareRelationship(false))), doctor, PatientOperations.Read, patient));
        Assert.False(await AllowedAsync(new PatientAuthorizationHandler(Services(new FakeCareRelationship(true))), doctor, PatientOperations.Update, patient));
        Assert.False(await AllowedAsync(new PatientAuthorizationHandler(Services(new FakeCareRelationship(true))), User(Roles.Medico), PatientOperations.Read, patient)); // sin identificador
    }

    // ---- Citas ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cita_recepcion_lee_y_cancela_el_medico_solo_lee_la_suya_y_el_paciente_lee_y_cancela_la_suya()
    {
        var handler = new AppointmentAuthorizationHandler();
        var appointment = NewAppointment();

        Assert.True(await AllowedAsync(handler, User(Roles.Recepcion, Guid.NewGuid()), AppointmentOperations.Read, appointment));
        Assert.True(await AllowedAsync(handler, User(Roles.Recepcion, Guid.NewGuid()), AppointmentOperations.Cancel, appointment));
        Assert.True(await AllowedAsync(handler, User(Roles.Medico, _doctorUser), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Medico, _doctorUser), AppointmentOperations.Cancel, appointment));
        Assert.True(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), AppointmentOperations.Read, appointment));
        Assert.True(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), AppointmentOperations.Cancel, appointment));
    }

    [Fact]
    public async Task Cita_ajena_o_de_roles_sin_acceso_se_deniega()
    {
        var handler = new AppointmentAuthorizationHandler();
        var appointment = NewAppointment();

        Assert.False(await AllowedAsync(handler, User(Roles.Medico, Guid.NewGuid()), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, Guid.NewGuid()), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Administrador, _doctorUser), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Laboratorio, _patientUser), AppointmentOperations.Read, appointment));
    }
}
