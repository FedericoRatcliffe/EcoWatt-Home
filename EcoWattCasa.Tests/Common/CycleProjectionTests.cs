using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Tests.Common;

/// <summary>
/// La proyeccion y la comparacion se apoyan en las horas medidas. Los casos de borde son los
/// que se vieron de verdad: el ciclo que arranca a la medianoche, y el ciclo anterior que quedo
/// con unas horas de datos despues de borrar el historial.
/// </summary>
public class CycleProjectionTests
{
    private static readonly PeriodWindow Cycle =
        new(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 30), "Ciclo 30/09 - 29/10", IsBillingCycle: true);

    [Fact]
    public void Una_hora_de_ciclo_no_proyecta()
    {
        // A la 1 de la manana del primer dia: 0,45 kWh en una hora. Contado como dia entero
        // daba 13,6 kWh para el ciclo.
        Assert.Null(CycleProjection.ProjectKwh(0.45, measuredHours: 1, totalDays: 30));
    }

    [Fact]
    public void Con_un_dia_entero_proyecta_por_hora_medida()
    {
        var projected = CycleProjection.ProjectKwh(6, measuredHours: 24, totalDays: 30);

        Assert.Equal(180, projected!.Value, precision: 6);
    }

    [Fact]
    public void Las_horas_sin_medir_no_bajan_el_ritmo()
    {
        // Diez dias de calendario con dos de API apagada: el ritmo sale de los ocho medidos.
        var projected = CycleProjection.ProjectKwh(40, measuredHours: 8 * 24, totalDays: 30);

        Assert.Equal(150, projected!.Value, precision: 6);
    }

    [Fact]
    public void Un_ciclo_con_horas_de_datos_no_se_compara()
    {
        // El ciclo del borrado: 4 horas medidas de 720.
        Assert.False(CycleProjection.IsComparable(4, Cycle));
    }

    [Fact]
    public void Un_corte_de_un_par_de_dias_no_impide_comparar()
    {
        Assert.True(CycleProjection.IsComparable(28 * 24, Cycle));
    }

    [Fact]
    public void El_borde_de_cobertura_es_el_noventa_por_ciento()
    {
        Assert.True(CycleProjection.IsComparable(648, Cycle));
        Assert.False(CycleProjection.IsComparable(647, Cycle));
    }
}
