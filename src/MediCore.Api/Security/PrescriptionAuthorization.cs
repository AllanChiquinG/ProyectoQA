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
/// Solo las lee el médico que las escribió y el propio paciente. Recepción, Administrador y Laboratorio no tienen acceso.
/// </summary>
public sealed class PrescriptionAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Prescription>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Prescription prescription)
    {
        var role = context.User.FindFirst(ClaimNames.Role)?.Value;
        var userId = context.User.UserId();

        if (userId is null || requirement != PrescriptionOperations.Read)
            return Task.CompletedTask;

        if ((role == Roles.Medico && prescription.Doctor.UserId == userId)
            || (role == Roles.Paciente && prescription.Patient.UserId == userId))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
