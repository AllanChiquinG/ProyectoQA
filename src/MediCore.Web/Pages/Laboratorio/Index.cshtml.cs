using MediCore.Contracts;
using MediCore.Contracts.Laboratory;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Laboratorio;

/// <summary>WEB-16: resultados de laboratorio con el estado de la notificación al paciente. Solo Laboratorio.</summary>
[Authorize(Roles = Roles.Laboratorio)]
public class IndexModel(IMediCoreApi api) : StaffPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedResult<LabResultSummaryDto> Result { get; private set; } = new([], 0, 1, 20);

    public string? Error { get; private set; }

    public bool HasPrevious => Result.Page > 1;

    public bool HasNext => Result.Page * Result.PageSize < Result.Total;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetLabResultsAsync(Q, Math.Max(PageNumber, 1), ct: ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;

        if (!result.IsOk)
            Error = "No se pudieron cargar los resultados. Inténtalo de nuevo.";
        else
            Result = result.Value!;
        return Page();
    }
}
