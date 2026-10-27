namespace MediCore.Domain.Scheduling;

/// <summary>
/// Bloque de atención semanal de un médico: días, hora de inicio y fin y duración de cada cita.
/// Las horas son del reloj de la clínica (sin zona horaria).
/// </summary>
public class DoctorSchedule
{
    /// <summary>Única duración de cita definida por el diseño aprobado.</summary>
    public const int SlotMinutes = 30;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DoctorId { get; set; }

    /// <summary>Días de atención como máscara de bits (bit 0 = domingo … bit 6 = sábado, igual que <see cref="DayOfWeek"/>).</summary>
    public int DaysMask { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static int ToMask(IEnumerable<DayOfWeek> days) => days.Distinct().Aggregate(0, (mask, day) => mask | (1 << (int)day));

    public static IReadOnlyList<DayOfWeek> FromMask(int mask)
        => Enum.GetValues<DayOfWeek>().Where(day => (mask & (1 << (int)day)) != 0).ToList();

    public bool IsWorkingDay(DayOfWeek day) => (DaysMask & (1 << (int)day)) != 0;

    /// <summary>Horarios de inicio de cita de un día de atención: desde el inicio, cada 30 minutos, mientras la cita termine antes del fin.</summary>
    public IReadOnlyList<TimeOnly> Slots()
    {
        var slots = new List<TimeOnly>();
        for (var t = StartTime; t.AddMinutes(SlotMinutes) <= EndTime && t.AddMinutes(SlotMinutes) > t; t = t.AddMinutes(SlotMinutes))
            slots.Add(t);
        return slots;
    }

    /// <summary>Devuelve el motivo por el que la configuración no es válida, o <c>null</c> si lo es.</summary>
    public static string? Validate(IReadOnlyCollection<DayOfWeek> days, TimeOnly start, TimeOnly end)
    {
        if (days.Count == 0) return "Selecciona al menos un día de atención.";
        if (end <= start) return "La hora final debe ser posterior a la inicial.";
        if (!IsOnGrid(start) || !IsOnGrid(end)) return "Las horas deben ser en punto o y media.";
        return null;
    }

    private static bool IsOnGrid(TimeOnly t) => t.Minute % SlotMinutes == 0 && t.Second == 0 && t.Millisecond == 0;
}
