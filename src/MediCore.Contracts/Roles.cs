namespace MediCore.Contracts;

/// <summary>Nombres de rol usados en tokens, políticas y contratos. Coinciden con <c>UserRole</c> del dominio.</summary>
public static class Roles
{
    public const string Recepcion = "Recepcion";
    public const string Medico = "Medico";
    public const string Laboratorio = "Laboratorio";
    public const string Administrador = "Administrador";
    public const string Paciente = "Paciente";

    /// <summary>Roles que usan la web del personal (el paciente usa solo la app móvil).</summary>
    public static readonly string[] Personal = [Recepcion, Medico, Laboratorio, Administrador];

    public static readonly string[] Todos = [.. Personal, Paciente];

    /// <summary>Nombre para mostrar en pantalla, con tildes.</summary>
    public static string Mostrar(string rol) => rol == Recepcion ? "Recepción" : rol == Medico ? "Médico" : rol;
}
