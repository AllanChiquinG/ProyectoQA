using System.Globalization;

namespace MediCore.Web.Services;

/// <summary>Fecha de la clínica y calendario mensual de la agenda. Debe usar la misma zona horaria que la API (<c>Clinic:TimeZone</c>).</summary>
public sealed class ClinicCalendar
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly int _maxDaysAhead;

    public ClinicCalendar(TimeProvider time, IConfiguration configuration)
    {
        _time = time;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Clinic:TimeZone"] ?? "America/Guatemala");
        _maxDaysAhead = configuration.GetValue("Scheduling:MaxDaysAhead", 90);
    }

    public DateTime Now => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime);

    public DateOnly LastBookableDate => Today.AddDays(_maxDaysAhead);

    /// <summary>Texto como en el diseño: "14 octubre, 2026".</summary>
    public static string DayLabel(DateOnly date) => date.ToString("d MMMM, yyyy", Spanish);

    /// <summary>Texto como en el diseño: "Octubre 2026".</summary>
    public static string MonthLabel(DateOnly date)
    {
        var text = date.ToString("MMMM yyyy", Spanish);
        return char.ToUpper(text[0], Spanish) + text[1..];
    }

    /// <summary>Semanas del mes (lunes a domingo) con los días de los meses vecinos para completar la cuadrícula.</summary>
    public static IReadOnlyList<(DateOnly Date, bool InMonth)> MonthGrid(DateOnly anyDayOfMonth)
    {
        var first = new DateOnly(anyDayOfMonth.Year, anyDayOfMonth.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // lunes = 0
        var start = first.AddDays(-offset);
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var cells = (int)Math.Ceiling((offset + daysInMonth) / 7.0) * 7;
        return Enumerable.Range(0, cells).Select(i => start.AddDays(i)).Select(d => (d, d.Month == first.Month)).ToList();
    }
}
