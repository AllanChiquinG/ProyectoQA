using System.Linq.Expressions;
using MediCore.Contracts;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Security;

public static class PatientOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
}

public static class PatientAccess
{
    /// <summary>
    /// Relación de atención del médico: el paciente tiene al menos una cita no cancelada con ese médico (la cuenta del médico
    /// es <c>Doctor.UserId</c>). Cuando existan las consultas (entrega 5) también cuentan. Es la única fuente de acceso del médico.
    /// </summary>
    public static Expression<Func<Patient, bool>> HasCareRelationship(MediCoreDbContext db, Guid doctorUserId)
        => patient => db.Appointments.Any(a => a.PatientId == patient.Id
                                               && a.Status == AppointmentStatus.Confirmed
                                               && a.Doctor.UserId == doctorUserId);
}

/// <summary>
/// Autorización a nivel de recurso: decide si ESTE usuario puede actuar sobre ESTE paciente.
/// Deniega por defecto. Un rol correcto no basta: un paciente solo accede a su propio registro y un médico solo a
/// pacientes con quienes tiene una cita registrada.
/// </summary>
public sealed class PatientAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<OperationAuthorizationRequirement, Patient>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Patient patient)
    {
        var role = context.User.FindFirst(ClaimNames.Role)?.Value;
        var userId = context.User.UserId();

        switch (role)
        {
            // Gestión administrativa de pacientes.
            case Roles.Administrador or Roles.Recepcion:
                context.Succeed(requirement);
                break;

            // El paciente solo puede leer su propio registro.
            case Roles.Paciente when requirement == PatientOperations.Read && userId is not null && patient.UserId == userId:
                context.Succeed(requirement);
                break;

            // El médico solo puede leer pacientes con quienes tiene una cita registrada.
            case Roles.Medico when requirement == PatientOperations.Read && userId is not null:
                // El contexto se obtiene solo cuando hace falta: esta clase se instancia en cada evaluación de autorización.
                var db = services.GetRequiredService<MediCoreDbContext>();
                if (await db.Patients.Where(p => p.Id == patient.Id).Where(PatientAccess.HasCareRelationship(db, userId.Value)).AnyAsync())
                    context.Succeed(requirement);
                break;

            // Laboratorio no accede a pacientes.
        }
    }
}
