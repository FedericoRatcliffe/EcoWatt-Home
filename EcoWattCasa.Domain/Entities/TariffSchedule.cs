namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Cuadro tarifario vigente a partir de <see cref="ValidFrom"/>. Modela una factura de
/// distribuidora argentina completa: cargo fijo diario, cargos variables por tramo de
/// consumo, recargos porcentuales sobre el importe basico (IVA, leyes provinciales) y
/// cargos fijos por periodo (alumbrado publico, renovables).
///
/// Los precios cambian casi todos los meses, asi que se guarda la serie completa y cada
/// dia del periodo se costea con el cuadro que estaba vigente ese dia.
/// </summary>
public class TariffSchedule
{
    public Guid Id { get; set; }

    /// <summary>Primer dia en que rige este cuadro.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Cargo fijo por dia de servicio, en ARS. La factura lo prorratea por dias.</summary>
    public decimal FixedChargePerDay { get; set; }

    /// <summary>De donde salio este cuadro: "Factura 09/2026", "carga manual", etc.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Tramos de consumo, ordenados de menor a mayor.</summary>
    public ICollection<TariffBlock> Blocks { get; set; } = new List<TariffBlock>();

    /// <summary>Recargos proporcionales al importe basico (IVA, Ley 10014, etc.).</summary>
    public ICollection<TariffSurcharge> Surcharges { get; set; } = new List<TariffSurcharge>();

    /// <summary>Cargos de monto fijo por periodo facturado (alumbrado publico, renovables).</summary>
    public ICollection<TariffPeriodCharge> PeriodCharges { get; set; } = new List<TariffPeriodCharge>();

    /// <summary>Suma de los recargos porcentuales. En la tarifa de Venado Tuerto da 0,42.</summary>
    public decimal TotalSurchargeRate => Surcharges.Sum(s => s.Rate);

    /// <summary>Precio del kWh numero <paramref name="kwhIndex"/> (base 0), sin recargos.</summary>
    public decimal PriceAt(double kwhIndex)
    {
        foreach (var block in Blocks.OrderBy(b => b.Order))
        {
            if (block.UpToKwh is null || kwhIndex < block.UpToKwh)
                return block.PricePerKwh;
        }

        return Blocks.OrderBy(b => b.Order).LastOrDefault()?.PricePerKwh ?? 0m;
    }
}

/// <summary>
/// Un tramo del cargo variable. <see cref="UpToKwh"/> es el tope acumulado del tramo
/// (75, 150, 300...), no su ancho; null significa "de ahi en adelante".
/// </summary>
public class TariffBlock
{
    public Guid Id { get; set; }
    public Guid TariffScheduleId { get; set; }
    public TariffSchedule? TariffSchedule { get; set; }

    public int Order { get; set; }
    public double? UpToKwh { get; set; }
    public decimal PricePerKwh { get; set; }

    /// <summary>Etiqueta tal como aparece en la factura.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>Recargo proporcional al importe basico. <see cref="Rate"/> es fraccion: 0,21 = 21%.</summary>
public class TariffSurcharge
{
    public Guid Id { get; set; }
    public Guid TariffScheduleId { get; set; }
    public TariffSchedule? TariffSchedule { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}

/// <summary>Cargo de monto fijo que se cobra una vez por periodo facturado.</summary>
public class TariffPeriodCharge
{
    public Guid Id { get; set; }
    public Guid TariffScheduleId { get; set; }
    public TariffSchedule? TariffSchedule { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
