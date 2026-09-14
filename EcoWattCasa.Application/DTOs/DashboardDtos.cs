namespace EcoWattCasa.Application.DTOs;

/// <summary>Consumo y costo de un dispositivo en un periodo.</summary>
/// <param name="Cost">
/// Parte del costo de energia que le toca segun su proporcion de kWh. Los cargos fijos de la
/// factura no se reparten entre dispositivos: no son de ninguno.
/// </param>
public sealed record DeviceConsumptionDto(
    Guid DeviceId,
    string Name,
    string Location,
    double Kwh,
    decimal Cost,
    double? CurrentWatts,
    double SharePercent);

/// <summary>Una linea de tramo de la factura.</summary>
public sealed record BillBlockDto(string Label, double Kwh, decimal PricePerKwh, decimal Amount);

/// <summary>Un cargo de la factura: porcentual sobre el basico, o monto fijo del periodo.</summary>
public sealed record BillChargeDto(string Name, decimal? Rate, decimal Amount);

/// <summary>La factura reconstruida, con el mismo desglose que el papel.</summary>
public sealed record BillDto(
    int Days,
    double TotalKwh,
    decimal FixedCharge,
    decimal VariableCharge,
    decimal BasicAmount,
    IReadOnlyList<BillBlockDto> Blocks,
    IReadOnlyList<BillChargeDto> Surcharges,
    IReadOnlyList<BillChargeDto> PeriodCharges,
    decimal TotalSurcharges,
    decimal Total,
    decimal MarginalPricePerKwh,
    decimal AveragePricePerKwh,
    decimal EnergyCost,
    decimal FixedCost);

/// <summary>Proyeccion del cierre del periodo si se mantiene el ritmo de consumo.</summary>
/// <param name="AveragePricePerKwh">
/// Precio medio al cierre. Es el numero comparable con la factura: el del periodo en curso
/// esta inflado porque reparte los cargos fijos sobre los kWh que van hasta ahora.
/// </param>
public sealed record ProjectionDto(
    int ElapsedDays,
    int TotalDays,
    double ProjectedKwh,
    decimal ProjectedCost,
    decimal AveragePricePerKwh);

/// <summary>Dashboard del dia: cards por dispositivo y curva horaria de la casa.</summary>
public sealed record DailyDashboardDto(
    DateOnly Date,
    double TotalKwh,
    decimal TotalCost,
    decimal MarginalPricePerKwh,
    IReadOnlyList<DeviceConsumptionDto> Devices,
    IReadOnlyList<HistoryPointDto> Hourly);

/// <summary>
/// Dashboard del periodo facturable: la factura completa, el desglose por dispositivo y la
/// curva diaria. <paramref name="IsBillingCycle"/> distingue el ciclo real del mes calendario.
/// </summary>
/// <param name="PreviousPeriodCost">Total del periodo anterior, completo.</param>
/// <param name="PreviousPeriodCostToDate">
/// Lo que iba el periodo anterior a esta misma altura. Es contra esto que se compara mientras
/// el periodo actual esta abierto: medio ciclo contra uno entero no dice nada.
/// </param>
/// <param name="ComparisonIsPartial">true mientras el periodo en curso no termino.</param>
public sealed record PeriodDashboardDto(
    string Label,
    DateOnly From,
    DateOnly To,
    bool IsBillingCycle,
    BillDto Bill,
    decimal PreviousPeriodCost,
    decimal PreviousPeriodCostToDate,
    bool ComparisonIsPartial,
    double? ChangePercentVsPreviousPeriod,
    ProjectionDto? Projection,
    IReadOnlyList<DeviceConsumptionDto> Devices,
    IReadOnlyList<HistoryPointDto> Daily);
