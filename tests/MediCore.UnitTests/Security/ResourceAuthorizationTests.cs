using System.Security.Claims;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Domain.Clinical;
using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace MediCore.UnitTests.Security;

/// <summary>Reglas de autorización por recurso (¿puede ESTE usuario con ESTE rol actuar sobre ESTE registro?) sin base de datos.</summary>
public class ResourceAuthorizationTests
{
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

    private static Prescription Prescription(Guid doctorUserId, Guid patientUserId) => new()
    {
        Doctor = new Doctor { FullName = "D", Specialty = "S", Phone = "1", Email = "d@example.com", UserId = doctorUserId },
        Patient = new Patient { FullName = "P", IdentificationNumber = "1", NormalizedIdentification = "1", Phone = "1", Email = "p@example.com", UserId = patientUserId },
    };

    private static Appointment Appointment(Guid doctorUserId, Guid patientUserId) => new()
    {
        Doctor = new Doctor { FullName = "D", Specialty = "S", Phone = "1", Email = "d@example.com", UserId = doctorUserId },
        Patient = new Patient { FullName = "P", IdentificationNumber = "1", NormalizedIdentification = "1", Phone = "1", Email = "p@example.com", UserId = patientUserId },
    };

    private readonly Guid _doctorUser = Guid.NewGuid();
    private readonly Guid _patientUser = Guid.NewGuid();

    // ---- Recetas ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Receta_la_lee_su_medico_y_su_paciente()
    {
        var handler = new PrescriptionAuthorizationHandler();
        var prescription = Prescription(_doctorUser, _patientUser);

        Assert.True(await AllowedAsync(handler, User(Roles.Medico, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.True(await AllowedAsync(handler, User(Roles.Paciente, _patientUser), PrescriptionOperations.Read, prescription));
    }

    [Theory]
    [InlineData(Roles.Recepcion)]
    [InlineData(Roles.Administrador)]
    [InlineData(Roles.Laboratorio)]
    public async Task Receta_no_la_leen_recepcion_administrador_ni_laboratorio_aunque_sean_el_mismo_usuario(string role)
    {
        var handler = new PrescriptionAuthorizationHandler();
        var prescription = Prescription(_doctorUser, _patientUser);

        Assert.False(await AllowedAsync(handler, User(role, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(role, _patientUser), PrescriptionOperations.Read, prescription));
    }

    [Fact]
    public async Task Receta_no_la_lee_otro_medico_ni_otro_paciente_ni_un_rol_cruzado()
    {
        var handler = new PrescriptionAuthorizationHandler();
        var prescription = Prescription(_doctorUser, _patientUser);

        Assert.False(await AllowedAsync(handler, User(Roles.Medico, Guid.NewGuid()), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, Guid.NewGuid()), PrescriptionOperations.Read, prescription));
        // El identificador del médico presentado con rol de paciente (y al revés) no sirve.
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, _doctorUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(Roles.Medico, _patientUser), PrescriptionOperations.Read, prescription));
        Assert.False(await AllowedAsync(handler, User(Roles.Medico), PrescriptionOperations.Read, prescription)); // sin identificador
        Assert.False(await AllowedAsync(handler, User(null, _doctorUser), PrescriptionOperations.Read, prescription)); // sin rol
    }

    // ---- Citas ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cita_recepcion_lee_y_cancela_el_medico_solo_lee_la_suya_y_el_paciente_lee_y_cancela_la_suya()
    {
        var handler = new AppointmentAuthorizationHandler();
        var appointment = Appointment(_doctorUser, _patientUser);

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
        var appointment = Appointment(_doctorUser, _patientUser);

        Assert.False(await AllowedAsync(handler, User(Roles.Medico, Guid.NewGuid()), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Paciente, Guid.NewGuid()), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Administrador, _doctorUser), AppointmentOperations.Read, appointment));
        Assert.False(await AllowedAsync(handler, User(Roles.Laboratorio, _patientUser), AppointmentOperations.Read, appointment));
    }
}
