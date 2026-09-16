using EcoWattCasa.Application.Billing;
using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// Lectura del PDF de la cooperativa.
///
/// Los fixtures son la salida literal de PdfPig sobre las cinco facturas reales: el papel
/// tiene dos columnas y el extractor las entrega invertidas y con los numeros pegados a su
/// etiqueta ("$3.539,0129 Dias" es importe 3.539,01 + 29 dias). El parser desarma eso, y lo
/// que saca se compara contra los mismos numeros de <see cref="RealBills"/>, transcriptos del
/// papel a mano.
/// </summary>
public class BillParserTests
{
    private static ParsedBill Parse(RealBills.Bill bill)
        => BillParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bills", bill.FixtureName)));

    private static RealBills.Bill Find(string period) => RealBills.AllBills().Single(b => b.Period == period);

    // ---------- Lo que el parser tiene que sacar de las cinco facturas ----------

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Lee_la_identificacion_de_la_factura(RealBills.Bill expected)
    {
        var parsed = Parse(expected);

        Assert.Equal(expected.Period, parsed.Period);
        Assert.Equal(expected.InvoiceNumber, parsed.InvoiceNumber);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Lee_el_renglon_del_medidor_aunque_venga_invertido_y_pegado(RealBills.Bill expected)
    {
        // "C 175 1 28.058,0031/07/2026 27.883,0002/07/2026"
        var parsed = Parse(expected);

        Assert.Equal(expected.ReadingFrom, parsed.ReadingFrom);
        Assert.Equal(expected.ReadingTo, parsed.ReadingTo);
        Assert.Equal(expected.Days, parsed.Days);
        Assert.Equal(expected.Kwh, parsed.Kwh);
        Assert.Equal(expected.MeterStart, parsed.MeterStart);
        Assert.Equal(expected.MeterEnd, parsed.MeterEnd);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Lee_los_importes_del_encabezado(RealBills.Bill expected)
    {
        var parsed = Parse(expected);

        Assert.Equal(expected.BasicAmount, parsed.BasicAmount);
        Assert.Equal(expected.TotalTaxes, parsed.TotalTaxes);
        Assert.Equal(expected.PrintedTotal, parsed.Total);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Los_kWh_de_los_tramos_suman_el_consumo_del_medidor(RealBills.Bill expected)
    {
        var parsed = Parse(expected);

        var billed = parsed.Details.Where(d => d.BlockOrder > 0).Sum(d => d.Quantity);

        Assert.Equal(expected.Kwh, billed, precision: 4);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Separa_los_impuestos_porcentuales_de_los_montos_fijos(RealBills.Bill expected)
    {
        var taxes = Parse(expected).Taxes;

        // IVA, Ley 10014, Cap.Rem y Cap.Inv son un porcentaje del importe basico.
        Assert.Equal(4, taxes.Count(t => t.IsProportional));

        // Alumbrado publico y renovables son montos fijos por factura.
        Assert.Equal(2, taxes.Count(t => !t.IsProportional));
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Los_impuestos_leidos_suman_el_subtotal_impreso(RealBills.Bill expected)
    {
        var parsed = Parse(expected);

        Assert.Equal(expected.TotalTaxes, parsed.Taxes.Sum(t => t.Amount));
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void No_toma_como_impuesto_los_renglones_de_energia(RealBills.Bill expected)
    {
        // La seccion I-ENERGIA tiene el mismo formato que II-IMPUESTOS; solo debe leerse la segunda.
        var taxes = Parse(expected).Taxes;

        Assert.DoesNotContain(taxes, t => t.Name.Contains("Cargo Fijo"));
        Assert.DoesNotContain(taxes, t => t.Name.Contains("Cargo Variable"));
        Assert.DoesNotContain(taxes, t => t.Name.Contains("Subtotal"));
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Ninguna_factura_deja_avisos_pendientes(RealBills.Bill expected)
    {
        // Un aviso significa que el parser vio algo que no supo clasificar.
        Assert.Empty(Parse(expected).Warnings);
    }

    // ---------- El circulo completo: PDF -> cuadros tarifarios -> factura recalculada ----------

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void El_cuadro_deducido_del_PDF_reproduce_el_total_de_su_propia_factura(RealBills.Bill expected)
    {
        var parsed = Parse(expected);
        var schedules = TariffScheduleFactory.Build(parsed, DateTimeOffset.UnixEpoch).Schedules;

        var rebuilt = BillCalculator.Build(parsed.ReadingFrom, parsed.ReadingTo, parsed.Kwh, schedules);

        Assert.InRange(rebuilt.Total, expected.PrintedTotal - 1m, expected.PrintedTotal + 1m);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Los_cuadros_deducidos_coinciden_con_los_transcriptos_a_mano(RealBills.Bill expected)
    {
        var deduced = TariffScheduleFactory.Build(Parse(expected), DateTimeOffset.UnixEpoch).Schedules;

        Assert.Equal(expected.Schedules.Count, deduced.Count);

        for (var i = 0; i < deduced.Count; i++)
        {
            Assert.Equal(expected.Schedules[i].ValidFrom, deduced[i].ValidFrom);
            Assert.Equal(expected.Schedules[i].FixedChargePerDay, deduced[i].FixedChargePerDay, precision: 3);

            var expectedPrices = expected.Schedules[i].Blocks.OrderBy(b => b.Order).Select(b => b.PricePerKwh);
            var deducedPrices = deduced[i].Blocks.OrderBy(b => b.Order).Select(b => b.PricePerKwh);
            Assert.Equal(expectedPrices, deducedPrices);
        }
    }

    // ---------- Casos especificos ----------

    [Fact]
    public void Una_factura_con_un_solo_precio_produce_un_solo_cuadro()
    {
        var schedules = TariffScheduleFactory.Build(Parse(Find("09/2026")), DateTimeOffset.UnixEpoch).Schedules;

        var schedule = Assert.Single(schedules);
        Assert.Equal(new DateOnly(2026, 7, 2), schedule.ValidFrom);
        // 3.539,01 repartido en 29 dias.
        Assert.Equal(122.0348m, schedule.FixedChargePerDay, precision: 3);
    }

    [Fact]
    public void Una_factura_con_aumento_a_mitad_de_periodo_produce_dos_cuadros()
    {
        // La 05/2026 reparte 30 dias en 26 + 4: el precio nuevo rige desde el 31/03.
        var schedules = TariffScheduleFactory.Build(Parse(Find("05/2026")), DateTimeOffset.UnixEpoch).Schedules;

        Assert.Equal(2, schedules.Count);
        Assert.Equal(new DateOnly(2026, 3, 5), schedules[0].ValidFrom);
        Assert.Equal(new DateOnly(2026, 3, 31), schedules[1].ValidFrom);
        Assert.Equal(214.19m, schedules[0].Blocks.OrderBy(b => b.Order).First().PricePerKwh);
        Assert.Equal(218.38m, schedules[1].Blocks.OrderBy(b => b.Order).First().PricePerKwh);
    }

    [Fact]
    public void El_precio_nuevo_de_una_factura_es_el_viejo_de_la_siguiente()
    {
        // Control de consistencia de la serie: la cooperativa encadena los precios mes a mes.
        var may = TariffScheduleFactory.Build(Parse(Find("05/2026")), DateTimeOffset.UnixEpoch).Schedules;
        var june = TariffScheduleFactory.Build(Parse(Find("06/2026")), DateTimeOffset.UnixEpoch).Schedules;

        var mayNewPrices = may[^1].Blocks.OrderBy(b => b.Order).Select(b => b.PricePerKwh).ToArray();
        var juneOldPrices = june[0].Blocks.OrderBy(b => b.Order).Select(b => b.PricePerKwh).ToArray();

        Assert.Equal(mayNewPrices, juneOldPrices);
    }

    [Fact]
    public void Las_tasas_de_los_recargos_dan_el_cuarenta_y_dos_por_ciento_en_todas()
    {
        foreach (var bill in RealBills.AllBills())
        {
            var schedule = TariffScheduleFactory.Build(Parse(bill), DateTimeOffset.UnixEpoch).Schedules[0];

            Assert.Equal(0.42m, schedule.TotalSurchargeRate);
            Assert.Equal(0.21m, schedule.Surcharges.Single(s => s.Name.Contains("IVA")).Rate);
            Assert.Equal(0.06m, schedule.Surcharges.Single(s => s.Name.Contains("10014")).Rate);
        }
    }

    [Fact]
    public void El_alumbrado_publico_cambia_entre_facturas_y_se_lee_de_cada_una()
    {
        decimal Lighting(string period) =>
            TariffScheduleFactory.Build(Parse(Find(period)), DateTimeOffset.UnixEpoch)
                .Schedules[0].PeriodCharges.Single(c => c.Name.Contains("Alum")).Amount;

        Assert.Equal(5_820.00m, Lighting("05/2026"));
        Assert.Equal(6_833.00m, Lighting("06/2026"));
        Assert.Equal(6_979.00m, Lighting("09/2026"));
    }

    [Fact]
    public void Un_texto_que_no_es_una_factura_avisa_en_vez_de_devolver_basura()
    {
        var ex = Assert.Throws<BillParseException>(() => BillParser.Parse("esto no es una factura"));

        Assert.Contains("periodo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
