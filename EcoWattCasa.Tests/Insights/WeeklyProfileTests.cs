using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Insights;

public class WeeklyProfileTests
{
    private static readonly TimeSpan Ar = TimeSpan.FromHours(-3);

    /// <summary>Un dia entero a consumo constante.</summary>
    private static IEnumerable<(DateTimeOffset, double)> Day(DateOnly date, double kwhPerHour)
        => Enumerable.Range(0, 24)
            .Select(h => (new DateTimeOffset(date.ToDateTime(new TimeOnly(h, 0)), Ar), kwhPerHour));

    // 2026-10-26 es lunes.
    private static readonly DateOnly Monday = new(2026, 10, 26);

    [Fact]
    public void Cada_celda_promedia_esa_hora_en_todas_las_semanas()
    {
        var hours = Day(Monday, 0.2).Concat(Day(Monday.AddDays(7), 0.4));

        var profile = WeeklyProfile.Build(hours);

        var cell = profile.Cells.Single(c => c.Day == 0 && c.Hour == 3);
        Assert.Equal(300d, cell.AvgWatts);
        Assert.Equal(2, cell.Samples);
    }

    [Fact]
    public void El_lunes_va_primero()
    {
        Assert.Equal(0, WeeklyProfile.DayIndex(DayOfWeek.Monday));
        Assert.Equal(6, WeeklyProfile.DayIndex(DayOfWeek.Sunday));
    }

    [Fact]
    public void Compara_dias_habiles_contra_fin_de_semana()
    {
        var week = Enumerable.Range(0, 7)
            .SelectMany(d => Day(Monday.AddDays(d), d < 5 ? 0.2 : 0.3));

        var profile = WeeklyProfile.Build(week);

        Assert.Equal(4.8d, profile.WeekdayKwh);
        Assert.Equal(7.2d, profile.WeekendKwh);
    }

    [Fact]
    public void Un_dia_con_horas_sin_medir_no_da_total()
    {
        // Sin las 23 h el martes daria corto: mejor no decir nada.
        var hours = Day(Monday, 0.2).Concat(Day(Monday.AddDays(1), 0.2).Take(23));

        var profile = WeeklyProfile.Build(hours);

        Assert.Equal(4.8d, profile.DayKwh[0]);
        Assert.Null(profile.DayKwh[1]);
        Assert.Null(profile.WeekdayKwh);
    }
}
