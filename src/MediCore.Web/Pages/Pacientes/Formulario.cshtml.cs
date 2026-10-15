using MediCore.Contracts;
using MediCore.Contracts.Patients;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

[Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador}")]
public class FormularioModel(IMediCoreApi api) : StaffPageModel
{
    [FromRoute]
    public Guid? Id { get; set; }

    [BindProperty]
    public PatientRequest Input { get; set; } = new("", "", null, "", "");

    public bool IsEdit => Id is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null) return Page();

        var result = await api.GetPatientAsync(Id.Value, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;
        if (!result.IsOk) return RedirectToPage("/Pacientes/Index");

        var p = result.Value!;
        Input = new PatientRequest(p.FullName, p.IdentificationNumber, p.BirthDate, p.Phone, p.Email);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var result = await api.SavePatientAsync(Id, Input, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;

        switch (result.Status)
        {
            case ApiStatus.Ok:
                TempData["Toast"] = "Datos del paciente guardados";
                return RedirectToPage("/Pacientes/Index");
            case ApiStatus.Conflict:
                ModelState.AddModelError("Input.IdentificationNumber", "Ya existe un paciente con esa identificación.");
                break;
            case ApiStatus.BadRequest:
                ModelState.AddModelError(string.Empty, "Revisa los datos ingresados.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "No se pudo guardar el paciente. Inténtalo de nuevo.");
                break;
        }

        return Page();
    }
}
