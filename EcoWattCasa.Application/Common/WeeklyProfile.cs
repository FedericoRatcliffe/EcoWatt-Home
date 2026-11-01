namespace EcoWattCasa.Application.Common;

/// <summary>Una celda del perfil: el promedio de una hora de un dia de la semana.</summary>
/// <param name="Day">0 = lunes ... 6 = domingo.</param>
/// <param name="Samples">Cuantas horas reales se promediaron. 0 = sin dato.</param>
public sealed record ProfileCell(int Day, int Hour, double AvgWatts, int Samples);

/// <summary>El perfil semanal y lo que dice de los dias habiles contra el fin de semana.</summary>
/// <param name="DayKwh">kWh de un dia tipico, de lunes a domingo. null si al dia le faltan horas.</param>
public sealed record WeeklyProfileResult(
    IReadOnlyList<ProfileCell> Cells,
    IReadOnlyList<double?> DayKwh,
    double? WeekdayKwh,
    double? WeekendKwh);

/// <summary>
/// La semana tipica de la casa, hora por hora: cada celda es el promedio de esa hora en todos
/// los lunes (o martes...) medidos. Muestra los habitos de un vistazo y responde "como cambia
/// la casa el fin de semana" sin tener que dedicarle un experimento.
///
/// La potencia sale de la energia de la hora (kWh de una hora = kW medios), no de la media de
/// watts: la energia viene del contador y es el dato confiable.
/// </summary>
public static class WeeklyProfile
{
    /// <param name="hours">Consumo de la casa por hora, con la hora ya en hora local.</param>
    public static WeeklyProfileResult Build(IEnumerable<(DateTimeOffset Local, double Kwh)> hours)
    {
        var sums = new double[7, 24];
        var counts = new int[7, 24];

        foreach (var (local, kwh) in hours)
        {
            var day = DayIndex(local.DayOfWeek);
            sums[day, local.Hour] += kwh;
            counts[day, local.Hour]++;
        }

        var cells = new List<ProfileCell>(7 * 24);
        var dayKwh = new List<double?>(7);

        for (var day = 0; day < 7; day++)
        {
            var total = 0d;
            var complete = true;

            for (var hour = 0; hour < 24; hour++)
            {
                var n = counts[day, hour];
                var avgKwh = n > 0 ? sums[day, hour] / n : 0d;
                cells.Add(new ProfileCell(day, hour, Math.Round(avgKwh * 1000d, 1), n));

                total += avgKwh;
                complete &= n > 0;
            }

            // Un dia tipico con horas sin medir daria corto: mejor no decir nada.
            dayKwh.Add(complete ? Math.Round(total, 2) : null);
        }

        return new WeeklyProfileResult(
            cells,
            dayKwh,
            Average(dayKwh.Take(5)),
            Average(dayKwh.Skip(5)));
    }

    /// <summary>Lunes primero, como se lee una semana aca.</summary>
    public static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>Promedio de los dias con dato; null si a alguno le falta, para no comparar a medias.</summary>
    private static double? Average(IEnumerable<double?> days)
    {
        var list = days.ToList();
        return list.All(d => d.HasValue) ? Math.Round(list.Average(d => d!.Value), 2) : null;
    }
}
