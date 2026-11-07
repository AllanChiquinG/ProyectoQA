using System.Text.Json;

namespace MediCore.Contracts;

/// <summary>Lee el mensaje para el usuario de una respuesta ProblemDetails de la API.</summary>
public static class ProblemDetailsReader
{
    /// <summary>Devuelve el primer error de validación o, si no hay, el campo <c>detail</c>.</summary>
    public static string? FirstMessage(JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            var first = errors.EnumerateObject().SelectMany(field => field.Value.EnumerateArray()).FirstOrDefault();
            if (first.ValueKind == JsonValueKind.String) return first.GetString();
        }

        return root.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }
}
