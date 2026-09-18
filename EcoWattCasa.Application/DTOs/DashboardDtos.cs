namespace EcoWattCasa.Application.DTOs;

/// <summary>Consumo y costo de un dispositivo en un periodo.</summary>
/// <param name="Cost">
/// Parte del costo de energia que le toca segun su proporcion de kWh. Los cargos fijos de la
/// factura no se reparten entre dispositivos: no son de ninguno.
/// </param>
/// <param name="IsUnidentified">
/// No es un dispositivo: es el consumo de la casa que no pasa por ningun enchufe medido.
/// Viaja como una fila mas para que el desglose sume el total, pero no tiene ficha propia y
/// su <see cref="DeviceId"/> es <see cref="Guid.Empty"/>.
/// </param>
public sealed record DeviceConsumptionDto(
    Guid DeviceId,
    string Name,
    string Location,
    double Kwh,
    decimal Cost,
    double? CurrentWatts,
    double SharePercent,
    bool IsUnidentified = false);

/// <summary>
/// Que parte del consumo de la casa se mide enchufe por enchufe y que parte no.
/// </summary>
/// <param name="HasMeter">
/// Hay medidor de tablero. Sin el, el total es solo la suma de los enchufes: mide de menos y
/// conviene decirlo en la pantalla.
/// </param>
/// <param name="MeasuredExceedsHouse">
/// Los enchufes midieron mas que el tablero, que no puede pasar. Senal de configuracion mal
/// puesta, no de consumo.
/// </param>
/// <param name="CurrentWatts">
/// Potencia de toda la casa ahora. Sale del medidor de tablero; sin medidor, de la suma de los
/// enchufes.
/// </param>
/// <param name="MeterDeviceIds">
/// Que dispositivos componen el total. El frontend los necesita para actualizar la potencia de
/// la casa con lo que llega por el hub, porque el medidor no tiene fila en el desglose.
/// </param>
public sealed record HouseSplitDto(
    double HouseKwh,
    double MeasuredKwh,
    double UnidentifiedKwh,
    double MeasuredSharePercent,
    bool HasMeter,
    bool MeasuredExceedsHouse,
    double CurrentWatts,
    IReadOnlyList<Guid> MeterDeviceIds);

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
    IReadOnlyList<HistoryPointDto> Hourly,
    HouseSplitDto House);

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
    IReadOnlyList<HistoryPointDto> Daily,
    HouseSplitDto House);
