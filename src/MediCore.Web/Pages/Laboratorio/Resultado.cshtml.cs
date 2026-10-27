using MediCore.Contracts;
using MediCore.Contracts.Laboratory;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Laboratorio;

/// <summary>WEB-18: detalle de un resultado. Lo ven Laboratorio y el médico con relación de atención; solo Laboratorio reintenta la notificación.</summary>
[Authorize(Roles = $"{Roles.Laboratorio},{Roles.Medico}")]
public class ResultadoModel(IMediCoreApi api) : StaffPageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public LabResultDto? Result { get; private set; }
    public string? Error { get; private set; }

    public bool IsLab => User.IsInRole(Roles.Laboratorio);
    public bool IsPending => Result?.NotificationStatus == NotificationStatuses.Pending;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetLabResultAsync(Id, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;
        if (!result.IsOk)
        {
            Error = "No se pudo cargar el resultado. Inténtalo de nuevo.";
            return Page();
        }

        Result = result.Value;
        return Page();
    }

    // Solo Laboratorio puede reprogramar: para otros roles la API responde 403 y se muestra "Acceso denegado".
    public async Task<IActionResult> OnPostReintentarAsync(CancellationToken ct)
    {
        var retry = await api.RetryLabNotificationAsync(Id, ct);
        if (await HandleCommonAsync(retry) is { } handled) return handled;

        TempData["Toast"] = !retry.IsOk
            ? "No se pudo reprogramar la notificación"
            : retry.Value!.Outcome == "AlreadySent" ? "La notificación ya fue enviada" : "Notificación reprogramada";
        return RedirectToPage("/Laboratorio/Resultado", new { id = Id });
    }
}
