using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Medicos;

[Authorize(Roles = Roles.Administrador)]
public class FormularioModel(IMediCoreApi api) : StaffPageModel
{
    [FromRoute]
    public Guid? Id { get; set; }

    [BindProperty]
    public DoctorRequest Input { get; set; } = new("", "", "", "");

    public bool IsEdit => Id is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null) return Page();

        var result = await api.GetDoctorAsync(Id.Value, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;
        if (!result.IsOk) return RedirectToPage("/Medicos/Index");

        var d = result.Value!;
        Input = new DoctorRequest(d.FullName, d.Specialty, d.Phone ?? "", d.Email ?? "");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var result = await api.SaveDoctorAsync(Id, Input, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;

        if (result.IsOk)
        {
            TempData["Toast"] = "Datos del médico guardados";
            return RedirectToPage("/Medicos/Index");
        }

        ModelState.AddModelError(string.Empty, result.Status == ApiStatus.BadRequest
            ? "Revisa los datos ingresados."
            : "No se pudo guardar el médico. Inténtalo de nuevo.");
        return Page();
    }
}
