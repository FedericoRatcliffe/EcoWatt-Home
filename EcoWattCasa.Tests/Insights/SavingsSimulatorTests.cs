using EcoWattCasa.Application.Common;
using EcoWattCasa.Tests.Billing;

namespace EcoWattCasa.Tests.Insights;

/// <summary>
/// "Cuanto ahorro si apago esto", sobre la factura real 09/2026 (175 kWh, tercer tramo). Lo que
/// se verifica es que el ahorro sale de reconstruir la factura y no de watts por precio medio.
/// </summary>
public class SavingsSimulatorTests
{
    private static readonly RealBills.Bill September =
        RealBills.AllBills().Single(b => b.Period == "09/2026");

    private static readonly PeriodWindow Cycle =
        new(September.ReadingFrom, September.ReadingTo, "Ciclo", IsBillingCycle: true);

    /// <summary>Watts que, prendidos todo el ciclo, suman exactamente esos kWh.</summary>
    private static double WattsFor(double kwh) => kwh * 1000d / (24d * Cycle.Days);

    private static SavingsResult Simulate(double baselineKwh, double watts, double hours = 24)
        => SavingsSimulator.Simulate(Cycle, baselineKwh, new ConsumptionChange(watts, hours), September.Schedules);

    [Fact]
    public void Sin_cambio_no_hay_ahorro()
    {
        var result = Simulate(175, 0);

        Assert.Equal(0m, result.Saving);
        Assert.InRange(result.Baseline.Total, September.PrintedTotal - 1m, September.PrintedTotal + 1m);
    }

    [Fact]
    public void Dentro_del_mismo_tramo_cada_kWh_ahorra_el_precio_marginal()
    {
        // 175 -> 165: sigue en el tramo de 150 a 300, asi que los 10 kWh salen todos de ahi.
        var result = Simulate(175, WattsFor(10));

        Assert.Equal(10d, result.DeltaKwh, 6);
        var expected = 10m * result.Baseline.MarginalPricePerKwh;
        Assert.InRange(result.Saving, expected - 2m, expected + 2m);
    }

    [Fact]
    public void Bajar_de_tramo_ahorra_menos_por_kWh_que_el_marginal_de_arriba()
    {
        // 160 -> 140: 10 kWh salen del tercer tramo y 10 del segundo, que es mas barato.
        var result = Simulate(160, WattsFor(20));

        Assert.True(result.Scenario.MarginalPricePerKwh < result.Baseline.MarginalPricePerKwh);
        Assert.True(result.Saving < 20m * result.Baseline.MarginalPricePerKwh);
        Assert.True(result.Saving > 20m * result.Scenario.MarginalPricePerKwh);
    }

    [Fact]
    public void Las_horas_por_dia_escalan_el_cambio()
    {
        var allDay = Simulate(175, 100, hours: 24);
        var halfDay = Simulate(175, 100, hours: 12);

        Assert.Equal(allDay.DeltaKwh / 2d, halfDay.DeltaKwh, 6);
    }

    [Fact]
    public void No_se_puede_consumir_menos_que_cero()
    {
        var result = Simulate(50, 5_000);

        Assert.Equal(0d, result.ScenarioKwh);
        Assert.Equal(50d, result.DeltaKwh);
        Assert.True(result.Scenario.Total > 0m, "los cargos fijos se pagan igual");
    }

    [Fact]
    public void Sumar_un_aparato_sube_la_factura()
    {
        var result = Simulate(175, -2_000, hours: 2);

        Assert.True(result.ScenarioKwh > 175d);
        Assert.True(result.Saving < 0m);
    }

    [Theory]
    [InlineData(0, 0, 24)]
    [InlineData(8, 20, 12)]
    [InlineData(22, 6, 8)]
    [InlineData(23, 0, 1)]
    public void Horas_de_una_franja_que_puede_cruzar_la_medianoche(int from, int to, double expected)
    {
        Assert.Equal(expected, SavingsSimulator.HoursBetween(from, to));
    }
}
