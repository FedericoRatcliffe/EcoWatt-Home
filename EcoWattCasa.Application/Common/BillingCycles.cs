namespace EcoWattCasa.Application.Common;

/// <summary>Ventana de un periodo del dashboard, ya resuelta a fechas locales.</summary>
/// <param name="Label">Como se muestra: "Ciclo 02/07 - 31/07" o "septiembre 2026".</param>
public sealed record PeriodWindow(DateOnly From, DateOnly To, string Label, bool IsBillingCycle)
{
    public int Days => To.DayNumber - From.DayNumber;

    /// <summary>Dias ya transcurridos del periodo a la fecha dada (para proyectar el cierre).</summary>
    public int ElapsedDays(DateOnly today)
        => Math.Clamp(today.DayNumber - From.DayNumber + 1, 0, Days);

    public bool Contains(DateOnly date) => date >= From && date < To;
}

/// <summary>
/// El ciclo de facturacion no es el mes calendario ni cae siempre el mismo dia: en las
/// facturas reales el medidor se leyo el 05/03, 04/04, 04/05, 02/06, 02/07 y 31/07, o sea
/// que la fecha se corre y el ciclo dura 29 o 30 dias.
///
/// Por eso el ciclo se ancla en la ultima lectura real (la que trae la factura importada) y
/// avanza de a 30 dias. Cada factura nueva vuelve a anclar, asi que el desvio no se acumula.
/// </summary>
public sealed class BillingCycles(HomeTimeZone tz)
{
    /// <summary>Largo nominal del ciclo. Las facturas reales dan 29 o 30 dias.</summary>
    public const int NominalCycleDays = 30;

    /// <summary>Dia de corte supuesto mientras no haya ninguna factura importada.</summary>
    public const int DefaultStartDay = 2;

    /// <summary>Ancla por defecto: el dia 2 del mes en curso.</summary>
    public DateOnly DefaultAnchor()
    {
        var today = tz.Today;
        var candidate = new DateOnly(today.Year, today.Month, DefaultStartDay);
        return candidate <= today ? candidate : candidate.AddMonths(-1);
    }

    /// <summary>Ciclo que contiene una fecha, contando de a 30 dias desde el ancla.</summary>
    public PeriodWindow CycleContaining(DateOnly date, DateOnly anchor)
    {
        var offset = (int)Math.Floor((date.DayNumber - anchor.DayNumber) / (double)NominalCycleDays);
        return Build(anchor.AddDays(offset * NominalCycleDays));
    }

    /// <summary>Ciclo actual desplazado <paramref name="offset"/> periodos (0 = el que corre).</summary>
    public PeriodWindow Cycle(int offset, DateOnly anchor)
    {
        var current = CycleContaining(tz.Today, anchor);
        return offset == 0 ? current : Build(current.From.AddDays(offset * NominalCycleDays));
    }

    public PeriodWindow CalendarMonth(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        return new PeriodWindow(from, to, $"{MonthName(month)} {year}", false);
    }

    /// <summary>Ventana UTC que cubre el periodo local.</summary>
    public (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ToUtc(PeriodWindow window)
        => (tz.StartOfDayUtc(window.From), tz.StartOfDayUtc(window.To));

    private static PeriodWindow Build(DateOnly from)
    {
        var to = from.AddDays(NominalCycleDays);
        return new PeriodWindow(from, to, $"Ciclo {from:dd/MM} - {to.AddDays(-1):dd/MM}", true);
    }

    private static readonly string[] Months =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    public static string MonthName(int month) => Months[Math.Clamp(month, 1, 12) - 1];
}
