using MediCore.Domain.Scheduling;

namespace MediCore.UnitTests.Scheduling;

public class SchedulingRulesTests
{
    private static DoctorSchedule Schedule(string start, string end, params DayOfWeek[] days)
        => new() { StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end), DaysMask = DoctorSchedule.ToMask(days) };

    [Fact]
    public void Los_horarios_son_cada_30_minutos_y_la_ultima_cita_termina_antes_del_fin()
    {
        var slots = Schedule("08:00", "12:00", DayOfWeek.Monday).Slots();

        Assert.Equal(8, slots.Count);
        Assert.Equal(new TimeOnly(8, 0), slots[0]);
        Assert.Equal(new TimeOnly(11, 30), slots[^1]);
    }

    [Fact]
    public void Un_bloque_de_una_sola_cita_genera_un_horario()
        => Assert.Single(Schedule("09:00", "09:30", DayOfWeek.Monday).Slots());

    [Fact]
    public void Un_bloque_hasta_las_23_30_no_da_la_vuelta_a_medianoche()
    {
        var slots = Schedule("23:00", "23:30", DayOfWeek.Monday).Slots();
        Assert.Equal([new TimeOnly(23, 0)], slots);
    }

    [Fact]
    public void La_mascara_de_dias_se_convierte_en_ambos_sentidos_sin_duplicados()
    {
        var mask = DoctorSchedule.ToMask([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Sunday]);

        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday], DoctorSchedule.FromMask(mask));
        var schedule = new DoctorSchedule { DaysMask = mask };
        Assert.True(schedule.IsWorkingDay(DayOfWeek.Wednesday));
        Assert.False(schedule.IsWorkingDay(DayOfWeek.Tuesday));
    }

    [Theory]
    [InlineData("08:00", "12:00", true)]
    [InlineData("08:00", "08:00", false)]
    [InlineData("12:00", "08:00", false)]
    [InlineData("08:15", "12:00", false)]
    [InlineData("08:00", "12:45", false)]
    public void Validacion_de_horas(string start, string end, bool valid)
        => Assert.Equal(valid, DoctorSchedule.Validate([DayOfWeek.Monday], TimeOnly.Parse(start), TimeOnly.Parse(end)) is null);

    [Fact]
    public void Sin_dias_el_horario_no_es_valido()
        => Assert.NotNull(DoctorSchedule.Validate([], new TimeOnly(8, 0), new TimeOnly(12, 0)));

    [Fact]
    public void La_cita_dura_30_minutos_desde_la_hora_elegida()
    {
        var (start, end) = Appointment.Range(new DateOnly(2026, 11, 3), new TimeOnly(9, 30));

        Assert.Equal(new DateTime(2026, 11, 3, 9, 30, 0), start);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0), end);
        Assert.Equal(DateTimeKind.Unspecified, start.Kind);
    }
}
