using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts;

/// <summary>Fecha de nacimiento válida: entre 1900 y hoy.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class BirthDateAttribute : ValidationAttribute
{
    public BirthDateAttribute() : base("La fecha de nacimiento no es válida.") { }

    public override bool IsValid(object? value)
        => value is null || (value is DateOnly date && date >= new DateOnly(1900, 1, 1) && date <= DateOnly.FromDateTime(DateTime.UtcNow));
}

public static class ValidationMessages
{
    public const string Required = "Este campo es obligatorio.";
    public const string Email = "Ingresa un correo electrónico válido.";
    public const string Phone = "Ingresa un teléfono válido (solo números, espacios, + - y paréntesis).";
    public const string PhonePattern = @"^[0-9+()\- ]{6,20}$";
}
