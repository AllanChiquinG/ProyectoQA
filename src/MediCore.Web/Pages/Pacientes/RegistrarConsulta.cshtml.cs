using System.ComponentModel.DataAnnotations;
using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Patients;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

/// <summary>WEB-13: registrar la consulta de una cita. El paciente, el médico y la fecha salen de la cita elegida.</summary>
[Authorize(Roles = Roles.Medico)]
public class RegistrarConsultaModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    public const string GuardarYCrearReceta = "rx";

    [FromRoute]
    public Guid Id { get; set; }

    /// <summary>Cita sugerida (si el expediente se abrió desde la agenda).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Cita { get; set; }

    [BindProperty]
    public ConsultaInput Input { get; set; } = new();

    /// <summary>Botón pulsado: "save" (guardar) o "rx" (guardar y crear receta).</summary>
    [BindProperty]
    public string? Action { get; set; }

    public PatientDto? Patient { get; private set; }
    public string DoctorName { get; private set; } = "";
    public IReadOnlyList<ConsultableAppointmentDto> Appointments { get; private set; } = [];
    public string? Error { get; private set; }
    public DateOnly Today => calendar.Today;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (await LoadAsync(ct) is { } failure) return failure;
        Input.AppointmentId ??= Appointments.FirstOrDefault(a => a.Id == Cita)?.Id ?? Appointments.FirstOrDefault()?.Id;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await LoadAsync(ct) is { } failure) return failure;
        if (!ModelState.IsValid) return Page();

        var result = await api.CreateConsultationAsync(new CreateConsultationRequest(Input.AppointmentId, Input.Reason, Input.Notes, Input.Diagnosis), ct);
        if (await HandleCommonAsync(result) is { } handled && result.Status != ApiStatus.NotFound) return handled;

        if (result.IsOk)
        {
            TempData["Toast"] = "Consulta guardada";
            return Action == GuardarYCrearReceta
                ? RedirectToPage("/Pacientes/CrearReceta", new { id = result.Value!.Id })
                : RedirectToPage("/Pacientes/Expediente", new { id = Id, tab = ExpedienteModel.TabConsultas });
        }

        Error = result.Message ?? result.Status switch
        {
            ApiStatus.Conflict => "Esta cita ya tiene una consulta registrada.",
            ApiStatus.NotFound => "La cita seleccionada no está disponible.",
            ApiStatus.Unavailable => "El servicio no está disponible en este momento. Inténtalo de nuevo.",
            _ => "No se pudo guardar la consulta. Revisa los datos e inténtalo de nuevo.",
        };
        return Page();
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken ct)
    {
        var patient = await api.GetPatientAsync(Id, ct);
        if (await HandleCommonAsync(patient) is { } handled) return handled;
        if (!patient.IsOk) return RedirectToPage("/Pacientes/Index");

        var me = await api.GetMyDoctorAsync(ct);
        if (await HandleCommonAsync(me) is { } doctorFailure) return doctorFailure;

        var appointments = await api.GetConsultableAppointmentsAsync(Id, ct);
        if (await HandleCommonAsync(appointments) is { } appointmentsFailure) return appointmentsFailure;

        Patient = patient.Value;
        DoctorName = me.Value?.FullName ?? "";
        Appointments = appointments.Value ?? [];
        return null;
    }

    public class ConsultaInput
    {
        [Required(ErrorMessage = "Selecciona la cita atendida.")]
        public Guid? AppointmentId { get; set; }

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(200, ErrorMessage = "Máximo 200 caracteres.")]
        public string Reason { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(4000, ErrorMessage = "Máximo 4000 caracteres.")]
        public string Notes { get; set; } = "";

        [Required(ErrorMessage = ValidationMessages.Required), StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
        public string Diagnosis { get; set; } = "";
    }
}
