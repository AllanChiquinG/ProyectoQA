using MediCore.Contracts;
using MediCore.Domain.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace MediCore.Api.Security;

public static class AppointmentOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Cancel = new() { Name = nameof(Cancel) };
}

/// <summary>
/// Autorización a nivel de recurso para citas (requiere cargar <c>Patient</c> y <c>Doctor</c>).
/// Recepción gestiona todas; el médico solo lee las de su propia agenda; el paciente solo lee y cancela las suyas.
/// </summary>
public sealed class AppointmentAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Appointment>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, Appointment appointment)
    {
        var role = context.User.FindFirst(ClaimNames.Role)?.Value;
        var userId = context.User.UserId();

        switch (role)
        {
            case Roles.Recepcion:
                context.Succeed(requirement);
                break;

            case Roles.Medico when requirement == AppointmentOperations.Read && userId is not null && appointment.Doctor.UserId == userId:
                context.Succeed(requirement);
                break;

            case Roles.Paciente when userId is not null && appointment.Patient.UserId == userId:
                context.Succeed(requirement);
                break;

            // Administrador, Laboratorio y cualquier otro caso: sin acceso.
        }

        return Task.CompletedTask;
    }
}
