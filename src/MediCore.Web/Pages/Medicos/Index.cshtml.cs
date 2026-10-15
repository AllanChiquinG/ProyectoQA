using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Medicos;

[Authorize(Roles = Roles.Administrador)]
public class IndexModel(IMediCoreApi api) : StaffPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<DoctorDto> Doctors { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetDoctorsAsync(Q, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;

        if (!result.IsOk)
            Error = "No se pudo cargar la lista de médicos. Inténtalo de nuevo.";
        else
            Doctors = result.Value!;
        return Page();
    }
}
