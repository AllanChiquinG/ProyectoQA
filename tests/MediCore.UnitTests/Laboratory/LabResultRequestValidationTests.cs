using System.ComponentModel.DataAnnotations;
using MediCore.Contracts.Laboratory;

namespace MediCore.UnitTests.Laboratory;

/// <summary>Reglas de validación del registro de un resultado (los mismos atributos que aplica la API).</summary>
public class LabResultRequestValidationTests
{
    private static readonly DateOnly Date = new(2026, 1, 10);

    private static LabResultParameterRequest Parameter(string name = "Hemoglobina", string value = "13.2") => new(name, value, "g/dL", "12–16");

    private static CreateLabResultRequest Valid() => new(Guid.NewGuid(), "Hematología", Date, "Obs", [Parameter()]);

    /// <summary>
    /// Valida un record como lo hace ASP.NET Core: los atributos de los parámetros del constructor se aplican al valor de cada parámetro
    /// (<see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?)"/> no los ve en un record posicional).
    /// </summary>
    private static List<string> ValidateRecord(object instance)
    {
        var errors = new List<string>();
        foreach (var parameter in instance.GetType().GetConstructors()[0].GetParameters())
        {
            var value = instance.GetType().GetProperty(parameter.Name!)!.GetValue(instance);
            var attributes = parameter.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).Cast<ValidationAttribute>().ToList();
            var results = new List<ValidationResult>();
            Validator.TryValidateValue(value!, new ValidationContext(instance) { MemberName = parameter.Name }, results, attributes);
            errors.AddRange(results.Select(r => r.ErrorMessage ?? ""));
        }

        return errors;
    }

    private static List<string> Errors(CreateLabResultRequest request)
    {
        var errors = ValidateRecord(request);
        foreach (var parameter in request.Parameters ?? [])
            errors.AddRange(ValidateRecord(parameter));
        return errors;
    }

    [Fact]
    public void Una_solicitud_completa_es_valida()
        => Assert.Empty(Errors(Valid()));

    [Fact]
    public void Faltan_paciente_examen_y_fecha()
    {
        var errors = Errors(new CreateLabResultRequest(null, " ", null, null, [Parameter()]));

        Assert.Equal(3, errors.Count(e => e == "Este campo es obligatorio."));
    }

    [Fact]
    public void Se_exige_al_menos_un_parametro_y_no_mas_de_diez()
    {
        Assert.Contains("Agrega al menos un parámetro.", Errors(Valid() with { Parameters = [] }));
        Assert.Contains("Un resultado admite hasta 10 parámetros.", Errors(Valid() with { Parameters = Enumerable.Repeat(Parameter(), 11).ToList() }));
        Assert.Empty(Errors(Valid() with { Parameters = Enumerable.Repeat(Parameter(), 10).ToList() }));
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("Nombre", "  ")]
    public void Nombre_y_valor_del_parametro_son_obligatorios(string name, string value)
        => Assert.Contains("Este campo es obligatorio.", Errors(Valid() with { Parameters = [Parameter(name, value)] }));

    [Fact]
    public void Los_limites_de_longitud_se_aplican()
    {
        Assert.Contains("Máximo 120 caracteres.", Errors(Valid() with { ExamType = new string('x', 121) }));
        Assert.Contains("Máximo 1000 caracteres.", Errors(Valid() with { Notes = new string('x', 1001) }));
        Assert.Contains("Máximo 80 caracteres.", Errors(Valid() with { Parameters = [Parameter(new string('n', 81))] }));
        Assert.Contains("Máximo 40 caracteres.", Errors(Valid() with { Parameters = [Parameter(value: new string('9', 41))] }));
    }

    [Fact]
    public void Unidad_y_rango_son_opcionales()
        => Assert.Empty(Errors(Valid() with { Parameters = [new LabResultParameterRequest("Embarazo", "Negativo", null, null)] }));
}
