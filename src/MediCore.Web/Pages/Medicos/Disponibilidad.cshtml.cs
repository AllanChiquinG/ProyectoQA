using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Scheduling;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Medicos;

[Authorize(Roles = Roles.Administrador)]
public class DisponibilidadModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    // Combinaciones de días del diseño aprobado (0 = domingo … 6 = sábado).
    public static readonly IReadOnlyList<(string Key, string Label, int[] Days)> Presets =
    [
        ("weekdays", "Lunes a viernes", [1, 2, 3, 4, 5]),
        ("mwf", "Lunes, miércoles y viernes", [1, 3, 5]),
        ("tt", "Martes y jueves", [2, 4]),
    ];

    public const string Custom = "custom";

    [FromRoute]
    public Guid Id { get; set; }

    [BindProperty]
    public string Preset { get; set; } = "weekdays";

    [BindProperty]
    public string StartTime { get; set; } = "08:00";

    [BindProperty]
    public string EndTime { get; set; } = "12:00";

    public DoctorDto? Doctor { get; private set; }
    public bool HasCustomDays { get; private set; }
    public string? Error { get; private set; }
    public string MonthText => ClinicCalendar.MonthLabel(calendar.Today);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (await LoadDoctorAsync(ct) is { } failure) return failure;

        var schedule = await api.GetScheduleAsync(Id, ct);
        if (await HandleCommonAsync(schedule) is { } handled && schedule.Status != ApiStatus.NotFound) return handled;

        // Sin horario guardado se ofrece el del diseño (lunes a viernes, 08:00 a 12:00).
        if (schedule.IsOk)
        {
            var days = schedule.Value!.Days.Order().ToArray();
            var preset = Presets.FirstOrDefault(p => p.Days.SequenceEqual(days));
            HasCustomDays = preset.Key is null;
            Preset = preset.Key ?? Custom;
            StartTime = schedule.Value.StartTime;
            EndTime = schedule.Value.EndTime;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await LoadDoctorAsync(ct) is { } failure) return failure;

        StartTime = StartTime?.Trim() is { Length: >= 5 } s ? s[..5] : "";
        EndTime = EndTime?.Trim() is { Length: >= 5 } e ? e[..5] : "";
        if (string.IsNullOrEmpty(StartTime) || string.IsNullOrEmpty(EndTime))
        {
            Error = "Indica la hora de inicio y de finalización.";
            return Page();
        }

        if (string.CompareOrdinal(EndTime, StartTime) <= 0)
        {
            Error = "La hora final debe ser posterior a la inicial.";
            HasCustomDays = Preset == Custom;
            return Page();
        }

        int[] days;
        if (Preset == Custom)
        {
            // Días configurados desde la API que el diseño no ofrece: se conservan.
            var current = await api.GetScheduleAsync(Id, ct);
            if (await HandleCommonAsync(current) is { } handled) return handled;
            days = current.Value!.Days.ToArray();
            HasCustomDays = true;
        }
        else
        {
            var preset = Presets.FirstOrDefault(p => p.Key == Preset);
            if (preset.Key is null)
            {
                Error = "Selecciona los días de atención.";
                return Page();
            }

            days = preset.Days;
        }

        var result = await api.SaveScheduleAsync(Id, new ScheduleRequest(days, StartTime, EndTime), ct);
        if (await HandleCommonAsync(result) is { } common && result.Status != ApiStatus.NotFound) return common;

        if (result.IsOk)
        {
            TempData["Toast"] = "Disponibilidad guardada";
            return RedirectToPage("/Medicos/Index");
        }

        Error = result.Message ?? "No se pudo guardar la disponibilidad. Inténtalo de nuevo.";
        HasCustomDays = Preset == Custom;
        return Page();
    }

    private async Task<IActionResult?> LoadDoctorAsync(CancellationToken ct)
    {
        var doctor = await api.GetDoctorAsync(Id, ct);
        if (await HandleCommonAsync(doctor) is { } handled) return handled;
        if (!doctor.IsOk) return RedirectToPage("/Medicos/Index");

        Doctor = doctor.Value;
        return null;
    }
}
