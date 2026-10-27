using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Patients;
using MediCore.Contracts.Scheduling;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Agenda;

[Authorize(Roles = Roles.Recepcion)]
public class ReservarModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    // Límite de la lista desplegable de pacientes (la API pagina; una búsqueda con autocompletar queda para la etapa de pulido).
    private const int PatientListLimit = 100;

    [BindProperty(SupportsGet = true)]
    public Guid? PatientId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? DoctorId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Time { get; set; }

    /// <summary>
    /// Clave de idempotencia de este formulario: si el envío se repite (doble clic, recarga, reintento de red) la API
    /// devuelve la misma cita en lugar de crear otra. Se renueva tras cualquier error para poder corregir y reenviar.
    /// </summary>
    [BindProperty]
    public string IdempotencyKey { get; set; } = "";

    public IReadOnlyList<PatientDto> Patients { get; private set; } = [];
    public IReadOnlyList<DoctorDto> Doctors { get; private set; } = [];
    public IReadOnlyList<SlotDto> Slots { get; private set; } = [];
    public bool IsWorkingDay { get; private set; } = true;
    public string? Error { get; private set; }
    public DateOnly Min => calendar.Today;
    public DateOnly Max => calendar.LastBookableDate;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await PrepareAsync(ct);

    /// <summary>Actualiza los horarios al cambiar paciente, médico o fecha, sin reservar.</summary>
    public async Task<IActionResult> OnPostActualizarAsync(CancellationToken ct) => await PrepareAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (PatientId is null || DoctorId is null || Date is null || string.IsNullOrWhiteSpace(Time))
        {
            Error = PatientId is null ? "Selecciona el paciente." : string.IsNullOrWhiteSpace(Time) ? "Selecciona un horario disponible." : "Completa los datos de la cita.";
            return await PrepareAsync(ct);
        }

        var request = new CreateAppointmentRequest(PatientId, DoctorId, Date, Time);
        var key = string.IsNullOrWhiteSpace(IdempotencyKey) ? Guid.NewGuid().ToString("N") : IdempotencyKey;
        var result = await api.CreateAppointmentAsync(request, key, ct);
        if (await HandleCommonAsync(result) is { } handled && result.Status != ApiStatus.NotFound) return handled;

        if (result.IsOk)
        {
            TempData["Toast"] = "Cita confirmada correctamente";
            return RedirectToPage("/Agenda/Cita", new { id = result.Value!.Id });
        }

        Error = result.Message ?? result.Status switch
        {
            ApiStatus.Conflict => "Este horario ya está ocupado. Selecciona otro para continuar.",
            ApiStatus.Unavailable => "El servicio no está disponible en este momento. Inténtalo de nuevo.",
            _ => "No se pudo reservar la cita. Revisa los datos e inténtalo de nuevo.",
        };
        Time = null; // el horario elegido ya no sirve; se conserva el resto de lo ingresado
        return await PrepareAsync(ct);
    }

    private async Task<IActionResult> PrepareAsync(CancellationToken ct)
    {
        var patients = await api.GetPatientsAsync(null, 1, PatientListLimit, ct);
        if (await HandleCommonAsync(patients) is { } p) return p;
        var doctors = await api.GetDoctorsAsync(null, ct);
        if (await HandleCommonAsync(doctors) is { } d) return d;
        if (!patients.IsOk || !doctors.IsOk)
        {
            Error ??= "No se pudo cargar el formulario. Inténtalo de nuevo.";
            return Page();
        }

        Patients = patients.Value!.Items;
        Doctors = doctors.Value!;
        DoctorId ??= Doctors.FirstOrDefault()?.Id;
        Date ??= calendar.Today;

        if (DoctorId is not null && Date is { } date)
        {
            var availability = await api.GetAvailabilityAsync(DoctorId.Value, date, ct);
            if (await HandleCommonAsync(availability) is { } a && availability.Status != ApiStatus.NotFound) return a;

            if (availability.IsOk)
            {
                IsWorkingDay = availability.Value!.IsWorkingDay;
                Slots = availability.Value.Slots;
                if (Time is not null && Slots.All(s => s.Time != Time || !s.Available)) Time = null;
            }
            else
            {
                Error ??= availability.Message ?? "No se pudo consultar la disponibilidad.";
            }
        }

        // Cada vista del formulario usa una clave nueva; los reenvíos del mismo envío comparten la suya.
        ModelState.Remove(nameof(IdempotencyKey));
        IdempotencyKey = Guid.NewGuid().ToString("N");
        return Page();
    }
}
