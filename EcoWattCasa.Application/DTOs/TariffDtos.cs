using System.ComponentModel.DataAnnotations;

namespace EcoWattCasa.Application.DTOs;

public sealed record TariffBlockDto(int Order, string Label, double? UpToKwh, decimal PricePerKwh);

public sealed record TariffSurchargeDto(string Name, decimal Rate);

public sealed record TariffPeriodChargeDto(string Name, decimal Amount);

/// <summary>Un cuadro tarifario completo, con la misma estructura que la factura.</summary>
public sealed record TariffScheduleDto(
    Guid Id,
    DateOnly ValidFrom,
    decimal FixedChargePerDay,
    string Source,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TariffBlockDto> Blocks,
    IReadOnlyList<TariffSurchargeDto> Surcharges,
    IReadOnlyList<TariffPeriodChargeDto> PeriodCharges,
    decimal TotalSurchargeRate,
    /// <summary>Precio del ultimo tramo con recargos: lo que cuesta un kWh adicional.</summary>
    decimal MarginalPricePerKwh);

public sealed class TariffBlockInput
{
    [Range(1, 20)]
    public int Order { get; set; }

    [StringLength(80)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Tope acumulado del tramo en kWh. null = de ahi en adelante.</summary>
    [Range(0.1, 100000)]
    public double? UpToKwh { get; set; }

    [Range(0, 1_000_000)]
    public decimal PricePerKwh { get; set; }
}

public sealed class TariffSurchargeInput
{
    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Fraccion, no porcentaje: 0,21 es 21%.</summary>
    [Range(0, 3)]
    public decimal Rate { get; set; }
}

public sealed class TariffPeriodChargeInput
{
    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 10_000_000)]
    public decimal Amount { get; set; }
}

public sealed class UpsertTariffDto
{
    /// <summary>Desde cuando rige. Si viene null, desde hoy.</summary>
    public DateOnly? ValidFrom { get; set; }

    [Range(0, 1_000_000)]
    public decimal FixedChargePerDay { get; set; }

    [StringLength(120)]
    public string Source { get; set; } = "Carga manual";

    [MinLength(1)]
    public List<TariffBlockInput> Blocks { get; set; } = [];

    public List<TariffSurchargeInput> Surcharges { get; set; } = [];

    public List<TariffPeriodChargeInput> PeriodCharges { get; set; } = [];
}

/// <summary>Una factura real ya cargada desde el PDF.</summary>
public sealed record ImportedBillDto(
    Guid Id,
    string InvoiceNumber,
    string Period,
    DateOnly ReadingFrom,
    DateOnly ReadingTo,
    int Days,
    double Kwh,
    decimal BasicAmount,
    decimal TotalTaxes,
    decimal Total,
    decimal AveragePricePerKwh,
    DateTimeOffset ImportedAt);

/// <summary>Resultado de importar un PDF: la factura leida y los cuadros tarifarios que dedujo.</summary>
public sealed record BillImportResultDto(
    ImportedBillDto Bill,
    IReadOnlyList<TariffScheduleDto> SchedulesCreated,
    /// <summary>Diferencia entre el total recalculado y el que dice el papel. Deberia ser centavos.</summary>
    decimal RecalculatedTotal,
    decimal DifferenceVsPrinted,
    IReadOnlyList<string> Warnings);
