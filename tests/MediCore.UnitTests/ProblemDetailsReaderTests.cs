using System.Text.Json;
using MediCore.Contracts;

namespace MediCore.UnitTests;

public class ProblemDetailsReaderTests
{
    private static string? Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ProblemDetailsReader.FirstMessage(document.RootElement);
    }

    [Fact]
    public void El_primer_error_de_validacion_tiene_prioridad_sobre_detail()
    {
        var message = Read("""{"errors":{"nombre":["Falta el nombre."]},"detail":"Solicitud inválida."}""");

        Assert.Equal("Falta el nombre.", message);
    }

    [Fact]
    public void Sin_errores_de_validacion_devuelve_detail()
    {
        Assert.Equal("Horario ocupado.", Read("""{"detail":"Horario ocupado."}"""));
    }

    [Fact]
    public void Con_errores_vacios_devuelve_detail()
    {
        Assert.Equal("Solicitud inválida.", Read("""{"errors":{},"detail":"Solicitud inválida."}"""));
    }

    [Fact]
    public void Sin_mensaje_devuelve_null()
    {
        Assert.Null(Read("""{"title":"Error"}"""));
    }
}
