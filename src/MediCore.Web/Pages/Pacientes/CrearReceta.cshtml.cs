using System.ComponentModel.DataAnnotations;
using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Patients;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

/// <summary>WEB-14: crear la receta de una consulta, con uno o varios medicamentos (hasta 10).</summary>
[Authorize(Roles = Roles.Medico)]
public class CrearRecetaModel(IMediCoreApi api) : StaffPageModel
{
    public const int MaxItems = 10;

    /// <summary>Identificador de la consulta a la que pertenece la receta.</summary>
    [FromRoute]
    public Guid Id { get; set; }

    [BindProperty]
    public List<MedicamentoInput> Items { get; set; } = [new()];

    public ConsultationDto? Consultation { get; private set; }
    public PatientDto? Patient { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ?? Page();

    /// <summary>Agrega una fila de medicamento sin perder lo escrito (funciona sin JavaScript).</summary>
    public async Task<IActionResult> OnPostAgregarAsync(CancellationToken ct)
    {
        ModelState.Clear();
        Items ??= [];
        if (Items.Count < MaxItems) Items.Add(new MedicamentoInput());
        return await LoadAsync(ct) ?? Page();
    }

    public async Task<IActionResult> OnPostQuitarAsync(int index, CancellationToken ct)
    {
        ModelState.Clear();
        Items ??= [];
        if (Items.Count > 1 && index >= 0 && index < Items.Count) Items.RemoveAt(index);
        return await LoadAsync(ct) ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Items ??= [];
        if (Items.Count == 0) Items.Add(new MedicamentoInput());
        if (await LoadAsync(ct) is { } failure) return failure;
        if (!ModelState.IsValid) return Page();

        var request = new CreatePrescriptionRequest(Id,
            Items.Select(i => new PrescriptionItemRequest(i.Medication, i.Dose, i.Frequency, i.Duration, i.Instructions)).ToList());
        var result = await api.CreatePrescriptionAsync(request, ct);
        if (await HandleCommonAsync(result) is { } handled && result.Status != ApiStatus.NotFound) return handled;

        if (result.IsOk)
        {
            TempData["Toast"] = "Receta guardada";
            return RedirectToPage("/Pacientes/Receta", new { id = result.Value!.Id });
        }

        Error = result.Message ?? result.Status switch
        {
            ApiStatus.NotFound => "La consulta ya no está disponible.",
            ApiStatus.Unavailable => "El servicio no está disponible en este momento. Inténtalo de nuevo.",
            _ => "No se pudo guardar la receta. Revisa los datos e inténtalo de nuevo.",
        };
        return Page();
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken ct)
    {
        var consultation = await api.GetConsultationAsync(Id, ct);
        if (await HandleCommonAsync(consultation) is { } handled) return handled;
        if (!consultation.IsOk) return RedirectToPage("/Pacientes/Index");

        var patient = await api.GetPatientAsync(consultation.Value!.PatientId, ct);
        if (await HandleCommonAsync(patient) is { } patientFailure) return patientFailure;

        Consultation = consultation.Value;
        Patient = patient.Value;
        return null;
    }

    public class MedicamentoInput
    {
        [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        public string Medication { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
        public string Dose { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
        public string Frequency { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
        public string Duration { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
        public string Instructions { get; set; } = "";
    }
}
