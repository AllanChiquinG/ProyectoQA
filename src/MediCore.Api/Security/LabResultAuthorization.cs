using MediCore.Contracts;
using MediCore.Domain.Laboratory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace MediCore.Api.Security;

public static class LabResultOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
}

/// <summary>
/// Autorización a nivel de recurso para resultados de laboratorio (requiere cargar <c>Patient</c>).
/// Laboratorio los lee todos (es quien los registra); el Médico solo los de pacientes con quienes tiene una relación válida de atención
/// (cita no cancelada o consulta registrada); el Paciente solo los suyos. Recepción y Administrador no tienen acceso clínico.
/// Los resultados no se modifican: no existe ninguna operación de edición ni de borrado.
/// </summary>
public sealed class LabResultAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<OperationAuthorizationRequirement, LabResult>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, LabResult result)
    {
        var role = context.User.FindFirst(ClaimNames.Role)?.Value;
        var userId = context.User.UserId();

        if (userId is null || requirement != LabResultOperations.Read)
            return;

        switch (role)
        {
            case Roles.Laboratorio:
                context.Succeed(requirement);
                break;
            case Roles.Paciente when result.Patient.UserId == userId:
                context.Succeed(requirement);
                break;
            case Roles.Medico when await services.GetRequiredService<ICareRelationship>().ExistsAsync(userId.Value, result.PatientId):
                context.Succeed(requirement);
                break;
        }
    }
}
