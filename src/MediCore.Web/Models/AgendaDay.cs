using MediCore.Contracts.Doctors;
using MediCore.Contracts.Scheduling;
using MediCore.Web.Services;

namespace MediCore.Web.Models;

/// <summary>Una fila de la agenda: un horario con su cita (si la hay) o libre/no disponible.</summary>
public record AgendaRow(string Time, AppointmentDto? Appointment, bool Available);

/// <summary>Datos que dibuja la agenda del día (Recepción WEB-02 y médico WEB-11).</summary>
public class AgendaDay
{
    public required DateOnly Date { get; init; }
    public required DateOnly Today { get; init; }
    public required Guid DoctorId { get; init; }
    public required string DoctorName { get; init; }
    public required bool IsWorkingDay { get; init; }
    public required IReadOnlyList<AgendaRow> Rows { get; init; }

    /// <summary>Página que muestra esta agenda (para los enlaces del calendario).</summary>
    public required string PagePath { get; init; }

    /// <summary>Recepción elige médico y puede reservar y abrir el detalle; el médico solo consulta su día.</summary>
    public required bool IsReception { get; init; }

    public IReadOnlyList<DoctorDto> Doctors { get; init; } = [];

    public int AppointmentCount => Rows.Count(r => r.Appointment is not null);
}

public static class AgendaBuilder
{
    /// <summary>
    /// Une los horarios de atención del médico con las citas del día. Las citas fuera del horario actual (por ejemplo, tras
    /// cambiarlo) también se muestran. Devuelve el primer error de la API si lo hay.
    /// </summary>
    public static async Task<(ApiResult Result, AgendaDay? Day)> BuildAsync(
        IMediCoreApi api, ClinicCalendar calendar, DoctorDto doctor, DateOnly date, string pagePath, bool isReception,
        IReadOnlyList<DoctorDto> doctors, CancellationToken ct)
    {
        var agenda = await api.GetAgendaAsync(isReception ? doctor.Id : null, date, ct);
        if (!agenda.IsOk) return (agenda, null);

        // La disponibilidad solo existe de hoy en adelante; los días pasados muestran únicamente las citas que hubo.
        var availability = new Dictionary<string, bool>();
        var working = false;
        if (date >= calendar.Today && date <= calendar.LastBookableDate)
        {
            var day = await api.GetAvailabilityAsync(doctor.Id, date, ct);
            if (!day.IsOk) return (day, null);
            working = day.Value!.IsWorkingDay;
            foreach (var slot in day.Value.Slots) availability[slot.Time] = slot.Available;
        }

        var appointments = agenda.Value!.ToDictionary(a => a.Time);
        var rows = availability.Keys.Union(appointments.Keys).Order()
            .Select(time => new AgendaRow(time, appointments.GetValueOrDefault(time), availability.GetValueOrDefault(time)))
            .ToList();

        return (new ApiResult(ApiStatus.Ok), new AgendaDay
        {
            Date = date, Today = calendar.Today, DoctorId = doctor.Id, DoctorName = doctor.FullName,
            IsWorkingDay = working || appointments.Count > 0, Rows = rows, PagePath = pagePath, IsReception = isReception, Doctors = doctors,
        });
    }
}
