namespace MediCore.Domain.Identity;

/// <summary>Roles del sistema. Los nombres deben coincidir con <c>MediCore.Contracts.Roles</c>.</summary>
public enum UserRole
{
    Recepcion = 1,
    Medico = 2,
    Laboratorio = 3,
    Administrador = 4,
    Paciente = 5,
}
