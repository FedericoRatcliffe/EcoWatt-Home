using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Application.Common;

/// <summary>
/// Como se reparte el consumo de la casa entre lo que se mide enchufe por enchufe y lo que no.
/// </summary>
/// <param name="HouseKwh">Total de la casa en el periodo.</param>
/// <param name="MeasuredKwh">Suma de los enchufes con medicion.</param>
/// <param name="UnidentifiedKwh">
/// Lo que consumio la casa y no paso por ningun enchufe medido: luces, termotanque, lo que
/// este enchufado en cualquier otro lado. Queda en cero si no hay medidor de tablero, porque
/// sin el no hay con que compararlo.
/// </param>
/// <param name="HasMeter">Hay un medidor de tablero instalado y activo.</param>
/// <param name="MeasuredExceedsHouse">
/// Los enchufes midieron mas que el tablero, que fisicamente no puede pasar. Es una senal de
/// configuracion mal puesta (un canal equivocado, un enchufe colgado de un circuito que el
/// medidor no ve) y conviene mostrarla en vez de esconderla detras de un cero.
/// </param>
public sealed record HouseSplit(
    double HouseKwh,
    double MeasuredKwh,
    double UnidentifiedKwh,
    bool HasMeter,
    bool MeasuredExceedsHouse);

/// <summary>
/// Decide cual es el consumo de toda la casa a partir de lo que reporta cada dispositivo.
///
/// La regla central: el medidor de tablero mide la acometida, o sea que **ya incluye** a los
/// enchufes. Sumar todos los dispositivos contaria dos veces lo que pasa por un enchufe medido.
/// Por eso el total sale del medidor y los enchufes solo sirven para desglosarlo.
///
/// Mientras no haya medidor instalado el total es la suma de los enchufes. Es menos que la casa
/// real, pero es lo unico que se mide, y permite que el sistema funcione antes de poner el EM2
/// en el tablero.
/// </summary>
public static class HouseConsumption
{
    /// <summary>
    /// Dispositivos cuyo consumo forma la serie de la casa. Con medidor de tablero es solo el
    /// medidor; sin el, todos los enchufes.
    /// </summary>
    public static HashSet<Guid> HouseSeriesDevices(IReadOnlyList<Device> devices)
    {
        var meters = devices
            .Where(d => d.IsActive && d.Role == DeviceRole.HouseMeter)
            .Select(d => d.Id)
            .ToHashSet();

        return meters.Count > 0
            ? meters
            : devices.Where(d => d.IsActive).Select(d => d.Id).ToHashSet();
    }

    /// <summary>
    /// Los enchufes que aportan al desglose: todo lo activo que no sea medidor de tablero.
    ///
    /// El filtro de activos tiene que ser el mismo que usa <see cref="Split"/>: si uno diera
    /// fila a un enchufe desactivado y el otro no lo contara como medido, sus kWh apareceria
    /// dos veces, en su fila y otra vez dentro del consumo no identificado.
    /// </summary>
    public static IEnumerable<Device> Appliances(IReadOnlyList<Device> devices)
        => devices.Where(d => d.IsActive && d.Role != DeviceRole.HouseMeter);

    /// <summary>
    /// Reparte el consumo entre lo medido por enchufe y lo no identificado.
    /// </summary>
    /// <param name="devices">Todos los dispositivos, medidor incluido.</param>
    /// <param name="kwhByDevice">kWh del periodo por dispositivo.</param>
    public static HouseSplit Split(IReadOnlyList<Device> devices, IReadOnlyDictionary<Guid, double> kwhByDevice)
    {
        double Kwh(Device d) => kwhByDevice.TryGetValue(d.Id, out var v) ? v : 0d;

        var active = devices.Where(d => d.IsActive).ToList();

        // Si hubiera mas de un medidor se suman: es el caso de una casa con mas de una fase,
        // donde cada medidor ve una parte de la acometida.
        var meters = active.Where(d => d.Role == DeviceRole.HouseMeter).ToList();
        var measured = active.Where(d => d.Role != DeviceRole.HouseMeter).Sum(Kwh);

        if (meters.Count == 0)
            return new HouseSplit(measured, measured, 0d, HasMeter: false, MeasuredExceedsHouse: false);

        var house = meters.Sum(Kwh);
        var unidentified = house - measured;

        // La tolerancia absorbe el ruido de dos contadores distintos leidos en momentos
        // distintos; recien por debajo de eso se considera que hay algo realmente mal.
        const double toleranceKwh = 0.001;

        return new HouseSplit(
            house,
            measured,
            Math.Max(0d, unidentified),
            HasMeter: true,
            MeasuredExceedsHouse: unidentified < -toleranceKwh);
    }
}
