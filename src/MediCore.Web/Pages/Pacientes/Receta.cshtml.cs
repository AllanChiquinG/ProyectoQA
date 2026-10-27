using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

/// <summary>WEB-15: detalle de una receta. La lee el médico que la escribió (el paciente la consulta en la app móvil).</summary>
[Authorize(Roles = Roles.Medico)]
public class RecetaModel(IMediCoreApi api) : StaffPageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public PrescriptionDto? Prescription { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetPrescriptionAsync(Id, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;
        if (!result.IsOk) return RedirectToPage("/Pacientes/Index");

        Prescription = result.Value;
        return Page();
    }
}
