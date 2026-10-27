using System.ComponentModel.DataAnnotations;
using MediCore.Contracts;
using MediCore.Contracts.Laboratory;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Laboratorio;

/// <summary>WEB-17: registrar un resultado con uno o varios parámetros (hasta 10). El resultado se guarda aunque la notificación quede pendiente.</summary>
[Authorize(Roles = Roles.Laboratorio)]
public class RegistrarModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    public const int MaxParameters = 10;

    /// <summary>Filtro opcional de la lista de pacientes (nombre o identificación).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public ResultadoInput Input { get; set; } = new();

    [BindProperty]
    public List<ParametroInput> Parameters { get; set; } = [new()];

    public IReadOnlyList<LabPatientDto> Patients { get; private set; } = [];
    public string? Error { get; private set; }
    public DateOnly Today => calendar.Today;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Input.PerformedOn = calendar.Today;
        return await LoadPatientsAsync(ct) ?? Page();
    }

    /// <summary>Agrega una fila de parámetro sin perder lo escrito (funciona sin JavaScript).</summary>
    public async Task<IActionResult> OnPostAgregarAsync(CancellationToken ct)
    {
        ModelState.Clear();
        Parameters ??= [];
        if (Parameters.Count < MaxParameters) Parameters.Add(new ParametroInput());
        return await LoadPatientsAsync(ct) ?? Page();
    }

    public async Task<IActionResult> OnPostQuitarAsync(int index, CancellationToken ct)
    {
        ModelState.Clear();
        Parameters ??= [];
        if (Parameters.Count > 1 && index >= 0 && index < Parameters.Count) Parameters.RemoveAt(index);
        return await LoadPatientsAsync(ct) ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Parameters ??= [];
        if (Parameters.Count == 0) Parameters.Add(new ParametroInput());
        if (await LoadPatientsAsync(ct) is { } failure) return failure;
        if (!ModelState.IsValid) return Page();

        var request = new CreateLabResultRequest(Input.PatientId, Input.ExamType, Input.PerformedOn, Input.Notes,
            Parameters.Select(p => new LabResultParameterRequest(p.Name, p.Value, p.Unit, p.ReferenceRange)).ToList());
        var result = await api.CreateLabResultAsync(request, ct);
        if (await HandleCommonAsync(result) is { } handled && result.Status != ApiStatus.NotFound) return handled;

        if (result.IsOk)
        {
            TempData["Toast"] = "Resultado guardado";
            return RedirectToPage("/Laboratorio/Resultado", new { id = result.Value!.Id });
        }

        Error = result.Message ?? result.Status switch
        {
            ApiStatus.Unavailable => "El servicio no está disponible en este momento. Inténtalo de nuevo.",
            _ => "No se pudo guardar el resultado. Revisa los datos e inténtalo de nuevo.",
        };
        return Page();
    }

    private async Task<IActionResult?> LoadPatientsAsync(CancellationToken ct)
    {
        var patients = await api.GetLabPatientsAsync(Q, ct);
        if (await HandleCommonAsync(patients) is { } handled) return handled;
        if (patients.IsOk) Patients = patients.Value!; else Error = "No se pudo cargar la lista de pacientes.";
        return null;
    }

    public class ResultadoInput
    {
        [Required(ErrorMessage = ValidationMessages.Required)]
        public Guid? PatientId { get; set; }

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        public string ExamType { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required)]
        public DateOnly? PerformedOn { get; set; }

        [StringLength(1000, ErrorMessage = "Máximo 1000 caracteres.")]
        public string? Notes { get; set; }
    }

    public class ParametroInput
    {
        [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string Value { get; set; } = "";

        [StringLength(20, ErrorMessage = "Máximo 20 caracteres.")]
        public string? Unit { get; set; }

        [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string? ReferenceRange { get; set; }
    }
}
