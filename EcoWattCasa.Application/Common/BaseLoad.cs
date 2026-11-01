using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Application.Common;

/// <summary>El piso de una noche: la hora mas baja de la madrugada.</summary>
/// <param name="HouseWatts">Piso de la casa. null si ninguna hora de la noche se midio entera.</param>
/// <param name="DeviceWatts">Piso de cada dispositivo, cada uno en su propia hora mas baja.</param>
public sealed record NightFloor(DateOnly Night, double? HouseWatts, IReadOnlyDictionary<Guid, double> DeviceWatts);

/// <summary>El consumo base de varias noches, resumido.</summary>
/// <param name="TypicalWatts">Mediana de las noches anteriores a la ultima. null con menos de tres.</param>
public sealed record BaseLoadSummary(
    int Nights,
    double? HouseWatts,
    double? LastNightWatts,
    double? TypicalWatts,
    IReadOnlyDictionary<Guid, double> DeviceWatts,
    IReadOnlyDictionary<Guid, double> DeviceLastNightWatts,
    IReadOnlyDictionary<Guid, double> DeviceTypicalWatts);

/// <summary>
/// Lo que la casa consume sin que nadie use nada: heladera, routers, standby, el DVR.
///
/// La ventana es de 00 a 08 h, pero no se promedia: se toma la hora mas baja. Alguien que mira
/// tele hasta la 1 sube esas horas y no las de las 4, asi que alcanza con que haya una hora
/// entera de casa quieta. Va por hora y no por minuto a proposito: la heladera prende y apaga
/// el compresor cada 20-40 min, y un minimo por minuto la agarraria apagada. La media de una
/// hora la incluye promediada, que es lo que de verdad cuesta.
///
/// Varias noches se resumen con la mediana, para que una noche rara (visitas, alguien que no
/// durmio) no mueva el numero.
/// </summary>
public static class BaseLoad
{
    /// <summary>Primera hora local de la ventana, inclusive.</summary>
    public const int NightStartHour = 0;

    /// <summary>Hora local en que termina la ventana, exclusive: la ultima hora es la de 7 a 8.</summary>
    public const int NightEndHour = 8;

    /// <summary>
    /// Cuanto de la hora tiene que estar cubierto por lecturas para contar. La media de una
    /// hora con la API caida 40 minutos es la de 20 minutos, y puede caer justo en un hueco
    /// de la heladera.
    /// </summary>
    public static readonly TimeSpan MinCoverage = TimeSpan.FromMinutes(45);

    /// <summary>Noches necesarias para hablar de "lo tipico" y comparar contra eso.</summary>
    public const int MinNightsForTypical = 3;

    /// <summary>Pisos noche por noche, de la mas vieja a la mas nueva.</summary>
    public static IReadOnlyList<NightFloor> Nights(
        IReadOnlyList<BucketEnergySamples> buckets,
        IReadOnlyList<Device> devices,
        TimeZoneInfo zone)
    {
        var houseDevices = HouseConsumption.HouseSeriesDevices(devices);
        var active = devices.Where(d => d.IsActive).Select(d => d.Id).ToHashSet();

        var nightly = buckets
            .Where(b => active.Contains(b.DeviceId) && IsCovered(b.Samples))
            .Select(b => (Bucket: b, Local: TimeZoneInfo.ConvertTime(b.BucketStartUtc, zone)))
            .Where(x => x.Local.Hour >= NightStartHour && x.Local.Hour < NightEndHour)
            .GroupBy(x => DateOnly.FromDateTime(x.Local.DateTime));

        var result = new List<NightFloor>();

        foreach (var night in nightly.OrderBy(g => g.Key))
        {
            var deviceWatts = night
                .GroupBy(x => x.Bucket.DeviceId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.Bucket.Samples.AvgWatts));

            // La casa solo cuenta en las horas en que estan todos los que la forman: con dos
            // medidores, una hora con uno solo daria un piso de la mitad.
            var houseHours = night
                .Where(x => houseDevices.Contains(x.Bucket.DeviceId))
                .GroupBy(x => x.Bucket.BucketStartUtc)
                .Where(g => g.Select(x => x.Bucket.DeviceId).Distinct().Count() == houseDevices.Count)
                .Select(g => g.Sum(x => x.Bucket.Samples.AvgWatts))
                .ToList();

            result.Add(new NightFloor(night.Key, houseHours.Count > 0 ? houseHours.Min() : null, deviceWatts));
        }

        return result;
    }

    public static BaseLoadSummary Summarize(IReadOnlyList<NightFloor> nights)
    {
        var withHouse = nights.Where(n => n.HouseWatts.HasValue).ToList();
        var last = nights.Count > 0 ? nights[^1] : null;

        var houseValues = withHouse.Select(n => n.HouseWatts!.Value).ToList();
        var previousHouse = withHouse.Take(withHouse.Count - 1).Select(n => n.HouseWatts!.Value).ToList();

        var deviceIds = nights.SelectMany(n => n.DeviceWatts.Keys).Distinct().ToList();

        var deviceMedian = new Dictionary<Guid, double>();
        var deviceLast = new Dictionary<Guid, double>();
        var deviceTypical = new Dictionary<Guid, double>();

        foreach (var id in deviceIds)
        {
            var values = nights
                .Where(n => n.DeviceWatts.ContainsKey(id))
                .Select(n => n.DeviceWatts[id])
                .ToList();

            deviceMedian[id] = Median(values)!.Value;

            if (last is not null && last.DeviceWatts.TryGetValue(id, out var lastWatts))
            {
                deviceLast[id] = lastWatts;

                var previous = nights
                    .Take(nights.Count - 1)
                    .Where(n => n.DeviceWatts.ContainsKey(id))
                    .Select(n => n.DeviceWatts[id])
                    .ToList();

                if (previous.Count >= MinNightsForTypical)
                    deviceTypical[id] = Median(previous)!.Value;
            }
        }

        return new BaseLoadSummary(
            withHouse.Count,
            Median(houseValues),
            // Solo vale como "anoche" si la ultima noche es la que tiene dato de la casa.
            last?.HouseWatts,
            last?.HouseWatts is not null && previousHouse.Count >= MinNightsForTypical ? Median(previousHouse) : null,
            deviceMedian,
            deviceLast,
            deviceTypical);
    }

    /// <summary>kWh de un ciclo nominal a una potencia constante.</summary>
    public static double CycleKwh(double watts)
        => watts * 24d * BillingCycles.NominalCycleDays / 1000d;

    private static bool IsCovered(EnergySamples samples)
        => samples is { FirstTimestamp: { } first, LastTimestamp: { } last } && last - first >= MinCoverage;

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;

        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2d;
    }
}
