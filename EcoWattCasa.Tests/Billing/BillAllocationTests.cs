using EcoWattCasa.Application.Common;
using EcoWattCasa.Tests.Billing;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// Como se reparte el costo de la factura: entre dispositivos (proporcional a sus kWh) y
/// entre dias del ciclo (cronologico, respetando los tramos).
///
/// Los dos repartos tienen el mismo invariante: la suma tiene que cerrar con la factura.
/// Si eso se rompe, los numeros del dashboard mienten aunque el total este bien.
/// </summary>
public class BillAllocationTests
{
    private static readonly RealBills.Bill September =
        RealBills.AllBills().Single(b => b.Period == "09/2026");

    private static BillBreakdown Build(double kwh) =>
        BillCalculator.Build(September.ReadingFrom, September.ReadingTo, kwh, September.Schedules);

    // ---------- Reparto entre dispositivos ----------

    [Fact]
    public void La_suma_de_los_dispositivos_da_el_costo_de_energia()
    {
        var bill = Build(175);
        double[] devices = [80, 50, 45]; // 175 kWh en total

        var allocated = devices.Sum(kwh => BillCalculator.AllocateToDevice(bill, kwh));

        // Tolerancia de un centavo: cada dispositivo se redondea por separado.
        Assert.InRange(allocated, bill.EnergyCostWithTaxes - 0.01m, bill.EnergyCostWithTaxes + 0.01m);
    }

    [Fact]
    public void Los_cargos_fijos_no_se_le_imputan_a_ningun_dispositivo()
    {
        var bill = Build(175);

        // Un dispositivo que consumio todo igual no carga con el alumbrado publico.
        var everything = BillCalculator.AllocateToDevice(bill, bill.TotalKwh);

        Assert.Equal(bill.EnergyCostWithTaxes, everything);
        Assert.True(bill.FixedCostWithTaxes > 0, "la factura tiene cargos fijos que no dependen del consumo");
        Assert.Equal(bill.Total, everything + bill.FixedCostWithTaxes);
    }

    [Fact]
    public void Un_dispositivo_sin_consumo_no_cuesta_nada()
    {
        Assert.Equal(0m, BillCalculator.AllocateToDevice(Build(175), 0));
    }

    [Fact]
    public void Sin_consumo_en_el_periodo_no_hay_nada_que_repartir()
    {
        Assert.Equal(0m, BillCalculator.AllocateToDevice(Build(0), 10));
    }

    // ---------- Reparto cronologico entre dias ----------

    [Fact]
    public void La_suma_de_los_dias_da_el_costo_de_energia()
    {
        var bill = Build(175);
        var closing = September.Schedules[^1];

        // 29 dias consumiendo parejo.
        var perDay = Enumerable.Repeat(175d / 29d, 29).ToList();
        var costs = BillCalculator.AllocateChronologically(bill, perDay, closing);

        Assert.InRange(costs.Sum(), bill.EnergyCostWithTaxes - 0.5m, bill.EnergyCostWithTaxes + 0.5m);
    }

    [Fact]
    public void Los_ultimos_dias_del_ciclo_salen_mas_caros_que_los_primeros()
    {
        var bill = Build(175);
        var closing = September.Schedules[^1];

        var perDay = Enumerable.Repeat(175d / 29d, 29).ToList();
        var costs = BillCalculator.AllocateChronologically(bill, perDay, closing);

        // Mismo consumo diario, pero el ultimo dia cae en el tramo 3 y el primero en el 1.
        Assert.True(
            costs[^1] > costs[0],
            $"el ultimo dia ({costs[^1]}) deberia costar mas que el primero ({costs[0]}) al cruzar de tramo");
    }

    [Fact]
    public void Dentro_de_un_mismo_tramo_todos_los_dias_cuestan_igual()
    {
        // 60 kWh en el ciclo: nunca se pasa de los 75 del primer tramo.
        var bill = Build(60);
        var closing = September.Schedules[^1];

        var perDay = Enumerable.Repeat(2d, 30).ToList();
        var costs = BillCalculator.AllocateChronologically(bill, perDay, closing);

        Assert.All(costs, c => Assert.Equal(costs[0], c));
    }

    [Fact]
    public void Un_dia_sin_consumo_no_cuesta_energia()
    {
        var bill = Build(100);
        var closing = September.Schedules[^1];

        var costs = BillCalculator.AllocateChronologically(bill, [50d, 0d, 50d], closing);

        Assert.Equal(0m, costs[1]);
    }

    // ---------- Periodo sin tarifa cargada ----------

    [Fact]
    public void Sin_ninguna_tarifa_la_factura_queda_en_cero_en_vez_de_explotar()
    {
        var bill = BillCalculator.Build(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 120, []);

        Assert.Equal(0m, bill.Total);
        Assert.Empty(bill.Blocks);
    }
}
