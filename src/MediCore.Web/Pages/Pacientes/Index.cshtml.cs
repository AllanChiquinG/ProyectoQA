using MediCore.Contracts;
using MediCore.Contracts.Patients;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

[Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador},{Roles.Medico}")]
public class IndexModel(IMediCoreApi api) : StaffPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedResult<PatientDto> Result { get; private set; } = new([], 0, 1, 20);

    public string? Error { get; private set; }

    public bool CanManage => User.IsInRole(Roles.Recepcion) || User.IsInRole(Roles.Administrador);

    public bool HasPrevious => Result.Page > 1;

    public bool HasNext => Result.Page * Result.PageSize < Result.Total;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetPatientsAsync(Q, Math.Max(PageNumber, 1), ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;

        if (!result.IsOk)
            Error = "No se pudo cargar la lista de pacientes. Inténtalo de nuevo.";
        else
            Result = result.Value!;
        return Page();
    }
}
