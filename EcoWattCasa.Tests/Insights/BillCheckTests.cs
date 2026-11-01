using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Insights;

/// <summary>
/// La factura contra lo medido. El error que se evita es mostrar como desvio del medidor lo que
/// en realidad es un periodo medido a medias.
/// </summary>
public class BillCheckTests
{
    private static readonly PeriodWindow Period =
        new(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 31), "08/2026", IsBillingCycle: true);

    private static int AllHours => Period.Days * 24;

    [Fact]
    public void Coincide_dentro_de_la_tolerancia()
    {
        var check = BillCheck.Compare(Period, 175, 168, AllHours);

        Assert.Equal(BillCheckStatus.Match, check.Status);
        Assert.Equal(-4d, check.DifferencePercent);
        Assert.Equal(100d, check.CoveragePercent);
    }

    [Fact]
    public void Medido_entero_y_lejos_de_la_factura_es_un_desvio()
    {
        var check = BillCheck.Compare(Period, 175, 140, AllHours);

        Assert.Equal(BillCheckStatus.Mismatch, check.Status);
        Assert.Equal(-20d, check.DifferencePercent);
    }

    [Fact]
    public void Medido_a_medias_no_se_juzga_al_medidor()
    {
        // Tres dias con la API caida: el -10 % es del hueco, no del EM2.
        var check = BillCheck.Compare(Period, 175, 157, AllHours - 72);

        Assert.Equal(BillCheckStatus.Partial, check.Status);
        Assert.True(check.CoveragePercent < 95d);
    }

    [Fact]
    public void Sin_lecturas_no_hay_comparacion()
    {
        var check = BillCheck.Compare(Period, 175, 0, 0);

        Assert.Equal(BillCheckStatus.NoData, check.Status);
        Assert.Null(check.DifferencePercent);
    }
}
