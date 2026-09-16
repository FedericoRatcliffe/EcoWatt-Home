using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// El test que sostiene todo el modelo de tarifa: reconstruir cada una de las cinco facturas
/// reales y comparar contra el total impreso.
///
/// La tolerancia es de un peso. Lo que queda de diferencia no es error del calculo: la
/// cooperativa redondea a pesos enteros los conceptos Cap.Rem.L.B.T. y Cap.Inv.Bienes de Uso,
/// y nosotros aplicamos el porcentaje exacto.
/// </summary>
public class BillGoldenTests
{
    private const decimal Tolerance = 1m;

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Reproduce_el_total_impreso_de_la_factura(RealBills.Bill bill)
    {
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        Assert.InRange(
            result.Total,
            bill.PrintedTotal - Tolerance,
            bill.PrintedTotal + Tolerance);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Reproduce_el_importe_basico(RealBills.Bill bill)
    {
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        Assert.InRange(
            result.BasicAmount,
            bill.BasicAmount - Tolerance,
            bill.BasicAmount + Tolerance);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Los_dias_del_periodo_salen_de_las_fechas_de_lectura(RealBills.Bill bill)
    {
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        Assert.Equal(bill.Days, result.Days);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void Los_tramos_reparten_todos_los_kWh_consumidos(RealBills.Bill bill)
    {
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        Assert.Equal(bill.Kwh, result.Blocks.Sum(b => b.Kwh), precision: 4);
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void El_total_se_parte_en_energia_mas_cargos_fijos(RealBills.Bill bill)
    {
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        // Invariante del dashboard: la suma de las cards (energia) mas los cargos fijos
        // tiene que dar la factura. Si esto se rompe, los numeros de la pantalla mienten.
        Assert.Equal(result.Total, result.EnergyCostWithTaxes + result.FixedCostWithTaxes);
    }

    [Fact]
    public void La_factura_de_septiembre_desglosa_igual_que_el_papel()
    {
        // Caso mas simple: un solo periodo de precio, 175 kWh en 29 dias.
        var bill = RealBills.AllBills().Single(b => b.Period == "09/2026");
        var result = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, bill.Kwh, bill.Schedules);

        Assert.Equal(3_539.01m, result.FixedCharge, precision: 1);

        var blocks = result.Blocks;
        Assert.Equal(3, blocks.Count);
        Assert.Equal(75, blocks[0].Kwh, precision: 4);
        Assert.Equal(18_279.75m, blocks[0].Amount, precision: 1);
        Assert.Equal(75, blocks[1].Kwh, precision: 4);
        Assert.Equal(19_875.75m, blocks[1].Amount, precision: 1);
        Assert.Equal(25, blocks[2].Kwh, precision: 4);
        Assert.Equal(8_858.50m, blocks[2].Amount, precision: 1);

        // IVA sobre el importe basico, no sobre el total.
        var vat = result.Surcharges.Single(s => s.Name.Contains("IVA"));
        Assert.Equal(10_616.13m, vat.Amount, precision: 1);

        Assert.Equal(6_979.00m, result.PeriodCharges.Single(c => c.Name.Contains("Alum")).Amount);
    }

    [Fact]
    public void El_precio_marginal_es_el_del_tramo_donde_cae_el_proximo_kWh()
    {
        var bill = RealBills.AllBills().Single(b => b.Period == "09/2026");

        // Con 175 kWh la casa esta en el tramo 3: $354,34 x 1,42 de recargos.
        var atEndOfCycle = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, 175, bill.Schedules);
        Assert.Equal(503.16m, atEndOfCycle.MarginalPricePerKwh, precision: 1);

        // Arrancando el ciclo esta en el tramo 1, mucho mas barato.
        var atStartOfCycle = BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, 20, bill.Schedules);
        Assert.Equal(346.10m, atStartOfCycle.MarginalPricePerKwh, precision: 1);
    }
}
