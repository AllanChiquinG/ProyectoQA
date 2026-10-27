using Microsoft.Extensions.Configuration;

namespace MediCore.Infrastructure.Scheduling;

/// <summary>Hora de la clínica. Las citas se guardan con el reloj local de la clínica; este servicio dice qué hora es allí.</summary>
public interface IClinicClock
{
    DateTime NowLocal { get; }
    DateOnly Today { get; }

    /// <summary>Último día en el que se puede reservar (configurable, 90 días por defecto).</summary>
    DateOnly LastBookableDate { get; }
}

internal sealed class ClinicClock : IClinicClock
{
    public const string DefaultTimeZone = "America/Guatemala";
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly int _maxDaysAhead;

    public ClinicClock(TimeProvider time, IConfiguration configuration)
    {
        _time = time;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Clinic:TimeZone"] ?? DefaultTimeZone);
        _maxDaysAhead = configuration.GetValue("Scheduling:MaxDaysAhead", 90);
    }

    public DateTime NowLocal => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime, DateTimeKind.Unspecified);

    public DateOnly Today => DateOnly.FromDateTime(NowLocal);

    public DateOnly LastBookableDate => Today.AddDays(_maxDaysAhead);
}
