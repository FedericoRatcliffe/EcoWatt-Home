namespace EcoWattCasa.Application.DTOs;

/// <summary>Lectura individual. Es tambien el payload que viaja por SignalR.</summary>
public sealed record EnergyReadingDto(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset TimestampUtc,
    double Watts,
    double Voltage,
    double Amperage,
    double? TotalKwh,
    double? TodayKwh,
    double? PowerFactor);

/// <summary>Historial de un dispositivo para la vista de detalle.</summary>
public sealed record DeviceHistoryDto(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    double TotalKwh,
    decimal TotalCost,
    IReadOnlyList<HistoryPointDto> Points);

/// <summary>Punto de un grafico de historial. <paramref name="BucketLocal"/> ya viene en hora de la casa.</summary>
public sealed record HistoryPointDto(
    DateTimeOffset BucketLocal,
    double Kwh,
    decimal Cost,
    double AvgWatts,
    double MaxWatts);
