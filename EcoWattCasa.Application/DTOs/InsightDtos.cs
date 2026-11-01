using EcoWattCasa.Application.Common;

namespace EcoWattCasa.Application.DTOs;

/// <summary>El consumo base de un enchufe, o del resto de la casa.</summary>
/// <param name="Watts">Piso tipico: mediana de las noches medidas.</param>
/// <param name="MonthlyCost">Lo que cuesta ese piso en un ciclo, al precio del kWh extra.</param>
/// <param name="TypicalWatts">Mediana de las noches anteriores a la ultima, para ver si subio.</param>
public sealed record BaseLoadDeviceDto(
    Guid DeviceId,
    string Name,
    double Watts,
    double MonthlyKwh,
    decimal MonthlyCost,
    double? LastNightWatts,
    double? TypicalWatts,
    bool IsUnidentified = false);

public sealed record BaseLoadNightDto(DateOnly Night, double? HouseWatts);

/// <summary>Lo que gasta la casa sin que nadie use nada. Ver <see cref="BaseLoad"/>.</summary>
/// <param name="HouseWatts">null mientras no haya ninguna noche medida.</param>
/// <param name="SharePercent">Que parte del consumo del ciclo es base.</param>
public sealed record BaseLoadDto(
    int Nights,
    int WindowStartHour,
    int WindowEndHour,
    double? HouseWatts,
    double? LastNightWatts,
    double? TypicalWatts,
    double MonthlyKwh,
    decimal MonthlyCost,
    decimal MarginalPricePerKwh,
    double? SharePercent,
    IReadOnlyList<BaseLoadDeviceDto> Devices,
    IReadOnlyList<BaseLoadNightDto> History);

/// <summary>Un cambio de consumo a simular sobre el ciclo en curso.</summary>
/// <param name="Watts">Positivo = se deja de consumir; negativo = se suma.</param>
/// <param name="FromHour">Hora local en que arranca, 0 a 23.</param>
/// <param name="ToHour">Hora local en que termina, 0 a 23. Igual a FromHour = todo el dia.</param>
/// <param name="BaselineKwh">Consumo del ciclo sobre el que simular. null = la proyeccion del ciclo en curso.</param>
public sealed record SimulationRequestDto(double Watts, int FromHour, int ToHour, double? BaselineKwh = null);

/// <param name="BaselineSource">De donde salio el consumo base, para mostrarlo.</param>
/// <param name="YearlySaving">El ahorro de un ciclo llevado a 12 meses, con la tarifa de hoy.</param>
public sealed record SimulationDto(
    string CycleLabel,
    string BaselineSource,
    double HoursPerDay,
    double BaselineKwh,
    double ScenarioKwh,
    double DeltaKwh,
    decimal BaselineTotal,
    decimal ScenarioTotal,
    decimal Saving,
    decimal YearlySaving,
    decimal BaselineMarginalPrice,
    decimal ScenarioMarginalPrice);

/// <summary>Una factura importada contra lo que midio el tablero.</summary>
public sealed record BillCheckDto(
    Guid BillId,
    string Period,
    DateOnly ReadingFrom,
    DateOnly ReadingTo,
    double BilledKwh,
    double MeasuredKwh,
    double CoveragePercent,
    double? DifferencePercent,
    BillCheckStatus Status);

/// <summary>La semana tipica de la casa. Ver <see cref="WeeklyProfile"/>.</summary>
/// <param name="From">Primer dia que entra al promedio.</param>
/// <param name="To">Dia siguiente al ultimo: hoy, que no entra porque no termino.</param>
public sealed record WeeklyProfileDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ProfileCell> Cells,
    IReadOnlyList<double?> DayKwh,
    double? WeekdayKwh,
    double? WeekendKwh);
