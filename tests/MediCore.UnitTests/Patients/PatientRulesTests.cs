using MediCore.Contracts;
using MediCore.Domain.Patients;

namespace MediCore.UnitTests.Patients;

public class PatientRulesTests
{
    [Theory]
    [InlineData("pac-001", "PAC-001")]
    [InlineData("  PAC 001 ", "PAC001")]
    [InlineData("1234 5678 9012", "123456789012")]
    public void La_identificacion_se_normaliza_sin_espacios_y_en_mayusculas(string input, string expected)
        => Assert.Equal(expected, Patient.NormalizeIdentification(input));

    [Fact]
    public void Fecha_de_nacimiento_acepta_pasado_y_rechaza_futuro_o_anterior_a_1900()
    {
        var attribute = new BirthDateAttribute();

        Assert.True(attribute.IsValid(new DateOnly(1994, 5, 18)));
        Assert.True(attribute.IsValid(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.False(attribute.IsValid(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))));
        Assert.False(attribute.IsValid(new DateOnly(1899, 12, 31)));
    }
}
