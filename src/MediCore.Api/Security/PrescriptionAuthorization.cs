using MediCore.Contracts;
using MediCore.Domain.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace MediCore.Api.Security;

public static class PrescriptionOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
}

/// <summary>
/// Autorización a nivel de recurso para recetas (requiere cargar <c>Patient</c> y <c>Doctor</c>).
/// Las lee su autor, cualquier médico con relación de atención con el paciente (expediente compartido) y el propio paciente.
/// Recepción, Administrador y Laboratorio no tienen acceso. Las recetas no se modifican: no existe ninguna operación de escritura
/// sobre una receta ya creada, ni siquiera para su autor.
/// </summary>
public sealed class PrescriptionAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<OperationAuthorizationRequirement, Prescription>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Prescription prescription)
    {
        var role = context.User.FindFirst(ClaimNames.Role)?.Value;
        var userId = context.User.UserId();

        if (userId is null || requirement != PrescriptionOperations.Read)
            return;

        if (role == Roles.Paciente && prescription.Patient.UserId == userId)
        {
            context.Succeed(requirement);
            return;
        }

        if (role != Roles.Medico)
            return;

        // El autor siempre; otro médico solo si tiene relación de atención con el paciente (cita no cancelada o consulta).
        if (prescription.Doctor.UserId == userId
            || await services.GetRequiredService<ICareRelationship>().ExistsAsync(userId.Value, prescription.PatientId))
            context.Succeed(requirement);
    }
}
