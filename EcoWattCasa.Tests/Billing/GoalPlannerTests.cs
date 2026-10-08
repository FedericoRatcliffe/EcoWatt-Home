using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// El objetivo de factura traducido a kWh. El caso de referencia es la factura real 09/2026:
/// 175 kWh dieron $78.995, asi que pedir ese mismo total tiene que devolver 175 kWh.
/// </summary>
public class GoalPlannerTests
{
    private static readonly RealBills.Bill September =
        RealBills.AllBills().Single(b => b.Period == "09/2026");

    private static readonly PeriodWindow SeptemberWindow =
        new(September.ReadingFrom, September.ReadingTo, "Ciclo 02/07 - 30/07", IsBillingCycle: true);

    private static double Budget(decimal amount, RealBills.Bill bill) =>
        GoalPlanner.KwhBudget(amount, bill.ReadingFrom, bill.ReadingTo, bill.Schedules);

    private static decimal Total(double kwh, RealBills.Bill bill) =>
        BillCalculator.Build(bill.ReadingFrom, bill.ReadingTo, kwh, bill.Schedules).Total;

    private static GoalProgress Evaluate(decimal amount, int elapsed, double consumed, decimal projected) =>
        GoalPlanner.Evaluate(amount, SeptemberWindow, elapsed, consumed, projected, September.Schedules);

    // ---------- Presupuesto de kWh ----------

    [Fact]
    public void El_total_de_una_factura_real_devuelve_sus_kWh()
    {
        Assert.Equal(175d, Budget(September.PrintedTotal, September));
    }

    [Fact]
    public void Bajar_de_80_mil_a_60_mil_es_consumir_un_cuarto_menos()
    {
        var budget = Budget(60_000m, September);

        // Los primeros 75 kWh y los cargos fijos no cambian: lo que se recorta sale todo del
        // segundo tramo. 175 -> ~133 kWh es un 24% menos de consumo para un 24% menos de plata.
        Assert.InRange(budget, 132d, 134d);
        Assert.True(Total(budget, September) <= 60_000m);
        Assert.True(Total(budget + 0.1d, September) > 60_000m);
    }

    [Fact]
    public void Un_objetivo_por_debajo_de_los_cargos_fijos_no_deja_ningun_kWh()
    {
        var minimum = Total(0, September);

        Assert.True(minimum > 10_000m, "la factura tiene cargos fijos aunque no se consuma nada");
        Assert.Equal(0d, Budget(minimum - 1m, September));
        Assert.Equal(0d, Budget(minimum, September));
    }

    [Theory]
    [MemberData(nameof(RealBills.All), MemberType = typeof(RealBills))]
    public void La_factura_del_presupuesto_nunca_pasa_el_objetivo(RealBills.Bill bill)
    {
        // Con dos cuadros en el periodo la factura reparte los tramos en kWh enteros y el total
        // no crece parejo. El presupuesto igual tiene que caer siempre adentro del objetivo.
        foreach (var amount in new[] { 25_000m, 48_000m, 60_000m, 75_000m, 120_000m })
        {
            var budget = Budget(amount, bill);
            Assert.True(Total(budget, bill) <= amount, $"{bill.Period}: {budget} kWh pasan los {amount}");
        }
    }

    [Fact]
    public void Mas_plata_alcanza_para_mas_kWh()
    {
        Assert.True(Budget(70_000m, September) > Budget(60_000m, September));
    }

    // ---------- Estado del ciclo ----------

    [Fact]
    public void Dice_cuanto_se_puede_consumir_por_dia_hasta_el_cierre()
    {
        // Dia 10 de 29 con 60 kWh: quedan ~73 kWh para 20 dias contando hoy.
        var goal = Evaluate(60_000m, elapsed: 10, consumed: 60, projected: 65_000m);

        Assert.True(goal.IsOpen);
        Assert.Equal(20, goal.DaysLeft);
        Assert.Equal(6d, goal.CurrentKwhPerDay);
        Assert.Equal(Math.Round(goal.RemainingKwh / 20d, 2), goal.AllowedKwhPerDay);
        Assert.InRange(goal.AllowedKwhPerDay!.Value, 3.5d, 3.8d);
    }

    [Fact]
    public void Proyectar_por_encima_del_objetivo_es_riesgo_no_fracaso()
    {
        var goal = Evaluate(60_000m, elapsed: 10, consumed: 60, projected: 65_000m);

        Assert.Equal(GoalStatus.AtRisk, goal.Status);
    }

    [Fact]
    public void Proyectar_por_debajo_es_ir_bien()
    {
        var goal = Evaluate(60_000m, elapsed: 10, consumed: 40, projected: 52_000m);

        Assert.Equal(GoalStatus.OnTrack, goal.Status);
    }

    [Fact]
    public void Haber_consumido_mas_que_el_presupuesto_ya_no_tiene_vuelta()
    {
        var goal = Evaluate(60_000m, elapsed: 25, consumed: 140, projected: 70_000m);

        Assert.Equal(GoalStatus.Exceeded, goal.Status);
        Assert.Equal(0d, goal.RemainingKwh);
        Assert.Equal(0d, goal.AllowedKwhPerDay);
    }

    [Fact]
    public void Un_objetivo_imposible_se_informa_como_tal()
    {
        var goal = Evaluate(5_000m, elapsed: 10, consumed: 0, projected: 12_000m);

        Assert.Equal(GoalStatus.Unreachable, goal.Status);
        Assert.True(goal.MinimumCost > 5_000m);
    }

    [Fact]
    public void Con_el_ciclo_cerrado_no_hay_ritmo_que_corregir()
    {
        var goal = Evaluate(80_000m, elapsed: 29, consumed: 175, projected: September.PrintedTotal);

        Assert.False(goal.IsOpen);
        Assert.Equal(0, goal.DaysLeft);
        Assert.Null(goal.AllowedKwhPerDay);
        Assert.Equal(GoalStatus.OnTrack, goal.Status);
    }
}
