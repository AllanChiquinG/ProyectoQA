using System.Globalization;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Caching;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MediCore.Infrastructure.Scheduling;

public record SlotState(TimeOnly Time, bool Available);

public record DayAvailability(bool IsWorkingDay, IReadOnlyList<SlotState> Slots);

public interface IAvailabilityService
{
    /// <summary>Horarios del médico en una fecha y cuáles siguen libres. Usa Redis; sin Redis consulta PostgreSQL.</summary>
    Task<DayAvailability> GetAsync(Guid doctorId, DateOnly date, CancellationToken ct = default);

    /// <summary>Invalida la disponibilidad de un día (al reservar o cancelar una cita).</summary>
    Task InvalidateDayAsync(Guid doctorId, DateOnly date, CancellationToken ct = default);

    /// <summary>Invalida todos los días de un médico (al cambiar su horario de atención).</summary>
    Task InvalidateDoctorAsync(Guid doctorId, CancellationToken ct = default);
}

internal sealed class AvailabilityService(MediCoreDbContext db, ICacheService cache, IClinicClock clock, IConfiguration configuration) : IAvailabilityService
{
    private const string TimeFormat = "HH:mm";

    // Lo que se guarda en Redis. No incluye lo que depende de la hora actual (horarios ya pasados): eso se calcula al responder.
    public sealed record CachedDay(bool IsWorkingDay, List<string> Slots, List<string> Occupied);

    private TimeSpan Ttl => configuration.GetValue("Cache:AvailabilityTtl", TimeSpan.FromMinutes(2));

    public async Task<DayAvailability> GetAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
    {
        var cached = await cache.GetOrCreateAsync(CacheKeys.Availability(doctorId, date), Ttl, token => LoadAsync(doctorId, date, token), ct);

        var now = clock.NowLocal;
        var occupied = cached.Occupied.ToHashSet();
        var slots = cached.Slots.Select(text =>
        {
            var time = TimeOnly.ParseExact(text, TimeFormat, CultureInfo.InvariantCulture);
            var isPast = date.ToDateTime(time) <= now;
            return new SlotState(time, !isPast && !occupied.Contains(text));
        }).ToList();

        return new DayAvailability(cached.IsWorkingDay, slots);
    }

    public Task InvalidateDayAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
        => cache.RemoveAsync(CacheKeys.Availability(doctorId, date), ct);

    public Task InvalidateDoctorAsync(Guid doctorId, CancellationToken ct = default)
        => cache.RemoveByPrefixAsync(CacheKeys.AvailabilityPrefix(doctorId), ct);

    private async Task<CachedDay> LoadAsync(Guid doctorId, DateOnly date, CancellationToken ct)
    {
        var schedule = await db.DoctorSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == doctorId, ct);
        if (schedule is null || !schedule.IsWorkingDay(date.DayOfWeek))
            return new CachedDay(false, [], []);

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var occupied = await db.Appointments.AsNoTracking()
            .Where(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Confirmed
                        && a.StartsAtLocal >= dayStart && a.StartsAtLocal < dayStart.AddDays(1))
            .Select(a => a.StartsAtLocal)
            .ToListAsync(ct);

        return new CachedDay(
            true,
            schedule.Slots().Select(t => t.ToString(TimeFormat, CultureInfo.InvariantCulture)).ToList(),
            occupied.Select(t => t.ToString(TimeFormat, CultureInfo.InvariantCulture)).ToList());
    }
}
