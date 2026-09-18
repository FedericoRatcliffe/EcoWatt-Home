using EcoWattCasa.Application.Alerts;
using EcoWattCasa.Application.Common;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Tests.Billing;

namespace EcoWattCasa.Tests.Alerts;

/// <summary>
/// Las reglas de alerta. Son una funcion pura del estado, asi que cada caso se arma entero
/// y se verifica sin base ni reloj.
///
/// La tarifa de referencia es la real (tramos 75 / 150 / 300 con 42% de recargos), porque el
/// valor de la alerta de cruce depende de que los precios sean los de verdad.
/// </summary>
public class AlertRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CycleStart = new(2026, 8, 30);

    private static readonly TariffSchedule Tariff = RealBills.Schedule(
        CycleStart, fixedPerDay: 122.035m,
        block1: 243.73m, block2: 265.01m, block3: 354.34m,
        publicLighting: 6_979m, renewables: 230.81m);

    private static readonly AlertThresholds Thresholds = new();

    private static PeriodWindow Cycle() =>
        new(CycleStart, CycleStart.AddDays(30), "Ciclo 30/08 - 28/09", IsBillingCycle: true);

    private static DeviceStatus Reporting(string name, int minutesAgo = 0) =>
        new(Guid.NewGuid(), name, IsActive: true, Now.AddMinutes(-minutesAgo));

    /// <summary>El medidor de tablero: su lectura es el total de la casa.</summary>
    private static DeviceStatus Meter(int minutesAgo = 0) =>
        new(Guid.NewGuid(), "Medidor de tablero", IsActive: true, Now.AddMinutes(-minutesAgo), IsHouseMeter: true);

    private static AlertContext Context(
        double cycleKwh = 50,
        double projectedKwh = 100,
        decimal projectedCost = 40_000m,
        decimal previousCost = 45_000m,
        int elapsedDays = 15,
        bool withTariff = true,
        IReadOnlyList<DeviceStatus>? devices = null)
        => new(
            Cycle(),
            elapsedDays,
            cycleKwh,
            projectedKwh,
            projectedCost,
            previousCost,
            withTariff ? Tariff : null,
            devices ?? [Reporting("PC"), Reporting("Heladera")]);

    private static IReadOnlyList<Alert> Evaluate(AlertContext context)
        => AlertRules.Evaluate(context, Thresholds, Now);

    private static Alert? Find(IReadOnlyList<Alert> alerts, string code)
        => alerts.FirstOrDefault(a => a.Code == code);

    // ---------- Cruce de tramo ----------

    [Fact]
    public void Avisa_cuando_la_proyeccion_cruza_el_tope_del_tramo()
    {
        // 50 kWh en 15 dias -> proyecta 100: cruza los 75 antes de cerrar.
        var alert = Find(Evaluate(Context(cycleKwh: 50, projectedKwh: 100)), "block-crossing");

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Contains("75", alert.Title);
    }

    [Fact]
    public void El_aviso_dice_a_cuanto_salta_el_kWh()
    {
        var alert = Find(Evaluate(Context(cycleKwh: 50, projectedKwh: 100)), "block-crossing");

        // 243,73 x 1,42 = 346,10 -> 265,01 x 1,42 = 376,31
        Assert.Contains("346", alert!.Detail);
        Assert.Contains("376", alert.Detail);
    }

    [Fact]
    public void El_salto_caro_al_tramo_tres_se_avisa_con_su_precio()
    {
        // Cruzar los 150 es el salto que importa: el kWh pasa de 265,01 a 354,34, y con el
        // 42% de recargos de 376 a 503. Es la unica alerta que puede ahorrar plata de verdad.
        var alert = Find(Evaluate(Context(cycleKwh: 100, projectedKwh: 200)), "block-crossing");

        Assert.NotNull(alert);
        Assert.Contains("150", alert.Title);
        Assert.Contains("376", alert.Detail);
        Assert.Contains("503", alert.Detail);
    }

    [Fact]
    public void No_avisa_de_cruzar_el_ultimo_tope_porque_no_se_sabe_a_cuanto_sube()
    {
        // La tarifa define hasta 300 kWh. Ninguna de las facturas reales llego a ese tramo,
        // asi que no hay precio cargado para el kWh 301: avisar seria inventarlo.
        Assert.Null(Find(Evaluate(Context(cycleKwh: 160, projectedKwh: 320)), "block-crossing"));
    }

    [Fact]
    public void Estima_la_fecha_del_cruce_extrapolando_el_consumo_diario()
    {
        // 50 kWh en 15 dias son 3,33 kWh/dia: los 75 llegan a los 23 dias del ciclo.
        var alert = Find(Evaluate(Context(cycleKwh: 50, projectedKwh: 100, elapsedDays: 15)), "block-crossing");

        // El ciclo arranca el 30/08; dia 23 cae el 21/09.
        Assert.Contains("21/09", alert!.Detail);
    }

    [Fact]
    public void No_avisa_si_la_proyeccion_no_llega_al_tope()
    {
        // 30 kWh en 15 dias -> proyecta 60: se queda en el primer tramo.
        Assert.Null(Find(Evaluate(Context(cycleKwh: 30, projectedKwh: 60)), "block-crossing"));
    }

    [Fact]
    public void No_avisa_sin_tarifa_cargada()
    {
        Assert.Null(Find(Evaluate(Context(withTariff: false)), "block-crossing"));
    }

    [Fact]
    public void No_avisa_al_arranque_del_ciclo_sin_consumo()
    {
        Assert.Null(Find(Evaluate(Context(cycleKwh: 0, projectedKwh: 0)), "block-crossing"));
    }

    [Fact]
    public void Pasado_el_ultimo_tope_no_hay_nada_mas_que_avisar()
    {
        // Mas de 300 kWh: no hay tramo siguiente definido.
        Assert.Null(Find(Evaluate(Context(cycleKwh: 350, projectedKwh: 700)), "block-crossing"));
    }

    // ---------- Telemetria ----------

    [Fact]
    public void Un_dispositivo_mudo_se_avisa_por_separado()
    {
        var silent = new DeviceStatus(Guid.NewGuid(), "Heladera", IsActive: true, Now.AddHours(-3));
        var alerts = Evaluate(Context(devices: [Reporting("PC"), silent]));

        var alert = Find(alerts, "device-silent");

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(silent.Id, alert.DeviceId);
        Assert.Contains("Heladera", alert.Title);
        Assert.Contains("3 horas", alert.Detail);
    }

    [Fact]
    public void Sin_medidor_de_tablero_un_enchufe_mudo_si_subestima_el_consumo()
    {
        // Midiendo solo enchufes, el que se calla se lleva su consumo: el total baja.
        var silent = new DeviceStatus(Guid.NewGuid(), "Heladera", IsActive: true, Now.AddHours(-3));

        var alert = Find(Evaluate(Context(devices: [Reporting("PC"), silent])), "device-silent");

        Assert.Contains("subestimado", alert!.Detail);
    }

    [Fact]
    public void Con_medidor_de_tablero_un_enchufe_mudo_no_cambia_el_total()
    {
        // El tablero mide la acometida igual: lo que consuma la heladera sigue contado, solo
        // que ya no se sabe que es de ella. Decir "queda subestimado" seria mentir.
        var silent = new DeviceStatus(Guid.NewGuid(), "Heladera", IsActive: true, Now.AddHours(-3));

        var alert = Find(Evaluate(Context(devices: [Meter(), Reporting("PC"), silent])), "device-silent");

        Assert.NotNull(alert);
        Assert.DoesNotContain("subestimado", alert.Detail);
        Assert.Contains("no identificado", alert.Detail);
    }

    [Fact]
    public void Que_se_calle_el_medidor_de_tablero_es_critico()
    {
        // Sin medidor no hay total de la casa, y sin total no hay factura estimada ni aviso de
        // cruce de tramo: es de otra gravedad que un enchufe mudo.
        var meter = Meter(minutesAgo: 180);

        var alert = Find(Evaluate(Context(devices: [meter, Reporting("PC")])), "meter-silent");

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(meter.Id, alert.DeviceId);
        Assert.Contains("toda la casa", alert.Detail);
    }

    [Fact]
    public void El_medidor_mudo_no_se_avisa_ademas_como_un_enchufe_mas()
    {
        var alerts = Evaluate(Context(devices: [Meter(minutesAgo: 180), Reporting("PC")]));

        Assert.Null(Find(alerts, "device-silent"));
    }

    [Fact]
    public void Si_callaron_todos_el_problema_es_del_broker_y_se_avisa_una_sola_vez()
    {
        var devices = new[]
        {
            new DeviceStatus(Guid.NewGuid(), "PC", true, Now.AddHours(-2)),
            new DeviceStatus(Guid.NewGuid(), "Heladera", true, Now.AddHours(-2))
        };

        var alerts = Evaluate(Context(devices: devices));

        var down = Find(alerts, "telemetry-down");
        Assert.NotNull(down);
        Assert.Equal(AlertSeverity.Critical, down.Severity);
        Assert.Contains("broker", down.Detail);

        // Una sola alerta, no una por aparato.
        Assert.Null(Find(alerts, "device-silent"));
    }

    [Fact]
    public void Un_dispositivo_inactivo_no_genera_alerta()
    {
        var disabled = new DeviceStatus(Guid.NewGuid(), "Viejo", IsActive: false, Now.AddDays(-30));

        var alerts = Evaluate(Context(devices: [Reporting("PC"), disabled]));

        Assert.Null(Find(alerts, "device-silent"));
        Assert.Null(Find(alerts, "telemetry-down"));
    }

    [Fact]
    public void Un_dispositivo_que_nunca_reporto_tambien_se_avisa()
    {
        var never = new DeviceStatus(Guid.NewGuid(), "Recien puesto", IsActive: true, LastSeenUtc: null);

        var alert = Find(Evaluate(Context(devices: [Reporting("PC"), never])), "device-silent");

        Assert.NotNull(alert);
        Assert.Contains("desde que se registro", alert.Detail);
    }

    [Fact]
    public void Los_textos_usan_singular_cuando_corresponde()
    {
        // "hace 1 hora", no "hace 1 horas": el mensaje lo lee una persona.
        var silent = new DeviceStatus(Guid.NewGuid(), "Heladera", IsActive: true, Now.AddHours(-1));

        var alert = Find(Evaluate(Context(devices: [Reporting("PC"), silent])), "device-silent");

        Assert.Contains("hace 1 hora.", alert!.Detail);
    }

    [Fact]
    public void Un_silencio_corto_no_alcanza_para_alertar()
    {
        // El umbral por defecto son 10 minutos.
        var alerts = Evaluate(Context(devices: [Reporting("PC"), Reporting("Heladera", minutesAgo: 5)]));

        Assert.Null(Find(alerts, "device-silent"));
    }

    [Fact]
    public void Sin_dispositivos_registrados_no_hay_alerta_de_telemetria()
    {
        Assert.Empty(Evaluate(Context(devices: [])).Where(a => a.Code is "device-silent" or "telemetry-down"));
    }

    // ---------- Proyeccion contra el ciclo anterior ----------

    [Fact]
    public void Avisa_si_la_proyeccion_supera_al_ciclo_anterior()
    {
        var alert = Find(Evaluate(Context(projectedCost: 60_000m, previousCost: 45_000m)), "cycle-overrun");

        Assert.NotNull(alert);
        Assert.Contains("33", alert.Detail); // 60.000 sobre 45.000 es 33,3% mas
    }

    [Fact]
    public void Un_exceso_chico_no_dispara_el_aviso()
    {
        // 10% de aumento, con umbral en 15%.
        Assert.Null(Find(Evaluate(Context(projectedCost: 49_500m, previousCost: 45_000m)), "cycle-overrun"));
    }

    [Fact]
    public void Sin_ciclo_anterior_no_hay_con_que_comparar()
    {
        Assert.Null(Find(Evaluate(Context(projectedCost: 60_000m, previousCost: 0m)), "cycle-overrun"));
    }

    // ---------- Orden y apagado ----------

    [Fact]
    public void Lo_mas_grave_aparece_primero()
    {
        var devices = new[]
        {
            new DeviceStatus(Guid.NewGuid(), "PC", true, Now.AddHours(-2)),
            new DeviceStatus(Guid.NewGuid(), "Heladera", true, Now.AddHours(-2))
        };

        var alerts = Evaluate(Context(devices: devices, projectedCost: 60_000m, previousCost: 45_000m));

        Assert.Equal(AlertSeverity.Critical, alerts[0].Severity);
    }

    [Fact]
    public void Con_las_alertas_apagadas_no_se_evalua_ninguna_regla()
    {
        var off = new AlertThresholds { Enabled = false };

        // Las reglas puras siguen respondiendo; el apagado lo aplica el servicio.
        // Aca se verifica que el umbral configurable efectivamente cambia el resultado.
        var strict = new AlertThresholds { SilenceMinutes = 1 };
        var context = Context(devices: [Reporting("PC"), Reporting("Heladera", minutesAgo: 5)]);

        Assert.Null(Find(AlertRules.Evaluate(context, off, Now), "device-silent"));
        Assert.NotNull(Find(AlertRules.Evaluate(context, strict, Now), "device-silent"));
    }
}
