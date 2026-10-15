using MediCore.Contracts;
using MediCore.Domain.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace MediCore.Api.Security;

public static class PatientOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
}

/// <summary>
/// Autorización a nivel de recurso: decide si ESTE usuario puede actuar sobre ESTE paciente.
/// Deniega por defecto. Un rol correcto no basta: un paciente solo accede a su propio registro.
/// </summary>
public sealed class PatientAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Patient>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Patient patient)
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

            // Médico: se autorizará por relación de atención (cita o consulta con ese paciente) cuando existan las citas
            // (entrega 4). Mientras tanto no se concede acceso. Laboratorio no accede a pacientes.
        }

        return Task.CompletedTask;
    }
}
