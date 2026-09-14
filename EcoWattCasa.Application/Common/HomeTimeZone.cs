namespace EcoWattCasa.Application.Common;

/// <summary>
/// Traduce entre la hora de la casa y UTC. Todo se guarda en UTC, pero "el consumo de hoy"
/// tiene que cortar a la medianoche de Buenos Aires, no a las 21 h.
/// </summary>
public sealed class HomeTimeZone
{
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    public TimeZoneInfo Zone { get; }

    public HomeTimeZone(string? timeZoneId = null)
    {
        Zone = Resolve(timeZoneId ?? DefaultTimeZoneId);
    }

    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // En Windows sin ICU el id IANA no resuelve; el id nativo si.
            try { return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("ART", TimeSpan.FromHours(-3), "ART", "ART"); }
        }
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    /// <summary>Instante UTC en que arranca una fecha local.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Ventana UTC [desde, hasta) que cubre un dia local completo.</summary>
    public (DateTimeOffset FromUtc, DateTimeOffset ToUtc) DayWindow(DateOnly date)
        => (StartOfDayUtc(date), StartOfDayUtc(date.AddDays(1)));

    /// <summary>Ventana UTC [desde, hasta) que cubre un mes local completo.</summary>
    public (DateTimeOffset FromUtc, DateTimeOffset ToUtc) MonthWindow(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        return (StartOfDayUtc(first), StartOfDayUtc(first.AddMonths(1)));
    }

    /// <summary>Ventana UTC de las ultimas N horas hasta ahora.</summary>
    public (DateTimeOffset FromUtc, DateTimeOffset ToUtc) LastHoursWindow(int hours)
    {
        var now = DateTimeOffset.UtcNow;
        return (now.AddHours(-hours), now);
    }
}
