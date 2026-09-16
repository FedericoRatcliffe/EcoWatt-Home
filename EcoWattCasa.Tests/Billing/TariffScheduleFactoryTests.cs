using EcoWattCasa.Application.Billing;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// Casos de borde de la construccion de cuadros tarifarios.
///
/// El camino feliz (una y dos vigencias, precios, cargo fijo diario, recargos) esta cubierto
/// en <see cref="BillParserTests"/> contra los cinco PDF reales. Aca quedan solo las
/// situaciones que esas facturas no producen: datos incompletos o inconsistentes.
/// </summary>
public class TariffScheduleFactoryTests
{
    /// <summary>Factura minima valida, para poder romperla de a un dato por vez.</summary>
    private static ParsedBill Bill(
        IReadOnlyList<BillDetailLine>? details = null,
        int days = 30)
        => new(
            InvoiceNumber: "0000-00000000",
            Period: "01/2026",
            ReadingFrom: new DateOnly(2026, 1, 1),
            ReadingTo: new DateOnly(2026, 1, 31),
            Days: days,
            Kwh: 100,
            MeterStart: 1_000,
            MeterEnd: 1_100,
            BasicAmount: 30_000m,
            TotalTaxes: 12_600m,
            Total: 42_600m,
            Details: details ??
            [
                new BillDetailLine("Cargo Fijo", 30, 1, 3_000m, 3_000m, BlockOrder: 0, UpToKwh: null),
                new BillDetailLine("Cargo Variable hasta 75 kWh", 30, 75, 200m, 15_000m, 1, 75),
                new BillDetailLine("Cargo Variable hasta 150 kWh", 30, 25, 250m, 6_250m, 2, 150)
            ],
            Taxes: [new BillTaxLine("IVA 21% Energia", 6_300m, IsProportional: true)],
            Warnings: []);

    [Fact]
    public void Si_los_dias_de_los_tramos_no_cierran_con_el_periodo_avisa()
    {
        // El papel dice 45 dias pero los renglones suman 30: las vigencias deducidas quedarian
        // corridas, y eso hay que mostrarlo en vez de guardarlo callado.
        var result = TariffScheduleFactory.Build(Bill(days: 45), DateTimeOffset.UnixEpoch);

        Assert.Contains(result.Warnings, w => w.Contains("vigencia"));
    }

    [Fact]
    public void Una_factura_sin_detalle_de_tarifas_no_inventa_cuadros()
    {
        var result = TariffScheduleFactory.Build(Bill(details: []), DateTimeOffset.UnixEpoch);

        Assert.Empty(result.Schedules);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Un_grupo_con_cargo_fijo_pero_sin_tramos_se_omite_con_aviso()
    {
        // Sin tramos no hay precio de energia: ese cuadro no sirve para costear nada.
        var result = TariffScheduleFactory.Build(
            Bill(details: [new BillDetailLine("Cargo Fijo", 30, 1, 3_000m, 3_000m, BlockOrder: 0, UpToKwh: null)]),
            DateTimeOffset.UnixEpoch);

        Assert.Empty(result.Schedules);
        Assert.Contains(result.Warnings, w => w.Contains("tramos variables"));
    }

    [Fact]
    public void Un_grupo_sin_cargo_fijo_igual_produce_cuadro_con_cargo_cero()
    {
        // Hay distribuidoras sin cargo fijo: no es un error, es otra estructura de tarifa.
        var result = TariffScheduleFactory.Build(
            Bill(details: [new BillDetailLine("Cargo Variable hasta 75 kWh", 30, 75, 200m, 15_000m, 1, 75)]),
            DateTimeOffset.UnixEpoch);

        var schedule = Assert.Single(result.Schedules);
        Assert.Equal(0m, schedule.FixedChargePerDay);
        Assert.Single(schedule.Blocks);
    }

    [Fact]
    public void Con_importe_basico_en_cero_las_tasas_no_dividen_por_cero()
    {
        var parsed = Bill() with { BasicAmount = 0m };

        var schedule = Assert.Single(TariffScheduleFactory.Build(parsed, DateTimeOffset.UnixEpoch).Schedules);

        Assert.All(schedule.Surcharges, s => Assert.Equal(0m, s.Rate));
    }

    [Fact]
    public void La_fecha_de_creacion_es_la_que_se_pasa_y_no_la_del_sistema()
    {
        // La fabrica es pura: sin reloj adentro, para que los tests sean deterministas.
        var stamp = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var schedule = Assert.Single(TariffScheduleFactory.Build(Bill(), stamp).Schedules);

        Assert.Equal(stamp, schedule.CreatedAt);
    }
}
