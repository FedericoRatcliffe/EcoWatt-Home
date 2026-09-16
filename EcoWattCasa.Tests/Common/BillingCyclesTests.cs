using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Common;

/// <summary>
/// El ciclo de facturacion. No es el mes calendario ni cae siempre el mismo dia: en las
/// facturas reales el medidor se leyo el 05/03, 04/04, 04/05, 02/06, 02/07 y 31/07.
/// Por eso se ancla en la ultima lectura real y avanza de a 30 dias.
/// </summary>
public class BillingCyclesTests
{
    private readonly HomeTimeZone _tz = new();
    private readonly BillingCycles _cycles;

    /// <summary>Fecha de lectura de la factura 09/2026, la mas reciente que hay cargada.</summary>
    private static readonly DateOnly Anchor = new(2026, 7, 31);

    public BillingCyclesTests() => _cycles = new BillingCycles(_tz);

    [Fact]
    public void El_ciclo_arranca_en_el_ancla()
    {
        var window = _cycles.CycleContaining(Anchor, Anchor);

        Assert.Equal(Anchor, window.From);
        Assert.Equal(Anchor.AddDays(30), window.To);
        Assert.Equal(30, window.Days);
    }

    [Theory]
    [InlineData(0)]   // el dia del ancla
    [InlineData(15)]  // mitad del ciclo
    [InlineData(29)]  // ultimo dia
    public void Cualquier_dia_del_ciclo_devuelve_la_misma_ventana(int dayOffset)
    {
        var window = _cycles.CycleContaining(Anchor.AddDays(dayOffset), Anchor);

        Assert.Equal(Anchor, window.From);
    }

    [Fact]
    public void El_dia_treinta_ya_es_el_ciclo_siguiente()
    {
        var window = _cycles.CycleContaining(Anchor.AddDays(30), Anchor);

        Assert.Equal(Anchor.AddDays(30), window.From);
    }

    [Fact]
    public void Una_fecha_anterior_al_ancla_cae_en_un_ciclo_previo()
    {
        // Un dia antes del ancla pertenece al ciclo que termino ahi.
        var window = _cycles.CycleContaining(Anchor.AddDays(-1), Anchor);

        Assert.Equal(Anchor.AddDays(-30), window.From);
        Assert.Equal(Anchor, window.To);
    }

    [Fact]
    public void El_offset_mueve_ciclos_enteros_hacia_atras()
    {
        var current = _cycles.Cycle(0, Anchor);
        var previous = _cycles.Cycle(-1, Anchor);

        Assert.Equal(current.From.AddDays(-30), previous.From);
        Assert.Equal(current.From, previous.To);
    }

    [Fact]
    public void El_ciclo_actual_contiene_el_dia_de_hoy()
    {
        var current = _cycles.Cycle(0, Anchor);

        Assert.True(current.Contains(_tz.Today), $"{_tz.Today} deberia caer en {current.Label}");
    }

    [Fact]
    public void El_mes_calendario_va_del_primero_al_primero()
    {
        var window = _cycles.CalendarMonth(2026, 9);

        Assert.Equal(new DateOnly(2026, 9, 1), window.From);
        Assert.Equal(new DateOnly(2026, 10, 1), window.To);
        Assert.Equal(30, window.Days);
        Assert.False(window.IsBillingCycle);
        Assert.Equal("septiembre 2026", window.Label);
    }

    [Fact]
    public void Los_dias_transcurridos_se_cuentan_desde_el_arranque_del_ciclo()
    {
        var window = _cycles.CycleContaining(Anchor, Anchor);

        Assert.Equal(1, window.ElapsedDays(Anchor));
        Assert.Equal(15, window.ElapsedDays(Anchor.AddDays(14)));
        // Una fecha posterior al cierre no puede dar mas dias que los del ciclo.
        Assert.Equal(30, window.ElapsedDays(Anchor.AddDays(60)));
    }
}

/// <summary>
/// Todo se guarda en UTC y se agrega en hora de Buenos Aires: "el consumo de hoy" tiene que
/// cortar a la medianoche local, no a las 21 h.
/// </summary>
public class HomeTimeZoneTests
{
    private readonly HomeTimeZone _tz = new();

    [Fact]
    public void Un_dia_local_arranca_a_las_tres_UTC()
    {
        var (from, to) = _tz.DayWindow(new DateOnly(2026, 9, 13));

        // Argentina esta en UTC-3 todo el ano (no tiene horario de verano desde 2009).
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero), from.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 3, 0, 0, TimeSpan.Zero), to.ToUniversalTime());
    }

    [Fact]
    public void La_ventana_de_un_dia_dura_veinticuatro_horas()
    {
        var (from, to) = _tz.DayWindow(new DateOnly(2026, 9, 13));

        Assert.Equal(TimeSpan.FromHours(24), to - from);
    }

    [Fact]
    public void La_ventana_de_un_mes_cubre_el_mes_local_completo()
    {
        var (from, to) = _tz.MonthWindow(2026, 9);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero), from.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero), to.ToUniversalTime());
    }

    [Fact]
    public void Un_instante_UTC_se_muestra_en_hora_local()
    {
        var utc = new DateTimeOffset(2026, 9, 13, 3, 30, 0, TimeSpan.Zero);

        var local = _tz.ToLocal(utc);

        Assert.Equal(0, local.Hour);
        Assert.Equal(30, local.Minute);
        Assert.Equal(13, local.Day);
    }
}
