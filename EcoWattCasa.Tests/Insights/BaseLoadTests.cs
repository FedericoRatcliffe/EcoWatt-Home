using EcoWattCasa.Application.Common;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Tests.Insights;

/// <summary>
/// El consumo base: lo que la casa gasta de madrugada sin que nadie use nada. Los numeros son
/// los de la noche real del 30/10: casa en 148 W, la zapatilla de Andy con 49 W fijos.
/// </summary>
public class BaseLoadTests
{
    private static readonly TimeZoneInfo BuenosAires =
        TimeZoneInfo.CreateCustomTimeZone("ART", TimeSpan.FromHours(-3), "ART", "ART");

    private static readonly Device Meter = new()
    {
        Id = Guid.NewGuid(), Name = "Medidor", Type = DeviceType.AthomEm2, Role = DeviceRole.HouseMeter
    };

    private static readonly Device Andy = new()
    {
        Id = Guid.NewGuid(), Name = "Zapatilla Andy 1", Type = DeviceType.AthomPlugV3, Role = DeviceRole.Appliance
    };

    /// <summary>Una hora entera medida, que empieza a la hora local dada.</summary>
    private static BucketEnergySamples Hour(Device device, DateOnly day, int localHour, double watts, int coveredMinutes = 59)
    {
        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(localHour, 0)), TimeSpan.FromHours(-3)).ToUniversalTime();
        return new BucketEnergySamples(
            start,
            device.Id,
            new EnergySamples(null, null, watts, watts, 100, start, start.AddMinutes(coveredMinutes)));
    }

    /// <summary>Una noche: tele prendida hasta las 2, despues casa quieta.</summary>
    private static IEnumerable<BucketEnergySamples> Night(DateOnly day, double house, double andy)
    {
        for (var h = 0; h < 8; h++)
        {
            var tv = h < 2 ? 120d : 0d;
            yield return Hour(Meter, day, h, house + tv);
            yield return Hour(Andy, day, h, andy + tv);
        }
    }

    private static readonly DateOnly Day1 = new(2026, 10, 25);

    [Fact]
    public void El_piso_es_la_hora_mas_baja_no_el_promedio_de_la_noche()
    {
        // La tele hasta las 2 sube esas horas; el promedio daria 178 W, la base es 148.
        var nights = BaseLoad.Nights(Night(Day1, 148, 49).ToList(), [Meter, Andy], BuenosAires);

        var night = Assert.Single(nights);
        Assert.Equal(Day1, night.Night);
        Assert.Equal(148d, night.HouseWatts);
        Assert.Equal(49d, night.DeviceWatts[Andy.Id]);
    }

    [Fact]
    public void Las_horas_fuera_de_la_madrugada_no_cuentan()
    {
        // A las 10 la casa puede estar vacia y en 90 W, pero eso no es la noche.
        var buckets = Night(Day1, 148, 49).Append(Hour(Meter, Day1, 10, 90)).ToList();

        var night = Assert.Single(BaseLoad.Nights(buckets, [Meter, Andy], BuenosAires));
        Assert.Equal(148d, night.HouseWatts);
    }

    [Fact]
    public void Una_hora_medida_a_medias_no_sirve_de_piso()
    {
        // Con la API caida 40 minutos la media sale de 20: puede caer con la heladera apagada.
        var buckets = Night(Day1, 148, 49).Append(Hour(Meter, Day1, 5, 60, coveredMinutes: 20)).ToList();

        var night = Assert.Single(BaseLoad.Nights(buckets, [Meter, Andy], BuenosAires));
        Assert.Equal(148d, night.HouseWatts);
    }

    [Fact]
    public void Varias_noches_se_resumen_con_la_mediana()
    {
        // Una noche con visitas (300 W) no mueve la base.
        var buckets = Night(Day1, 148, 49)
            .Concat(Night(Day1.AddDays(1), 150, 49))
            .Concat(Night(Day1.AddDays(2), 300, 49))
            .Concat(Night(Day1.AddDays(3), 146, 48))
            .Concat(Night(Day1.AddDays(4), 149, 49))
            .ToList();

        var summary = BaseLoad.Summarize(BaseLoad.Nights(buckets, [Meter, Andy], BuenosAires));

        Assert.Equal(5, summary.Nights);
        Assert.Equal(149d, summary.HouseWatts);
        Assert.Equal(49d, summary.DeviceWatts[Andy.Id]);
    }

    [Fact]
    public void La_ultima_noche_se_compara_contra_las_anteriores()
    {
        var buckets = Night(Day1, 148, 49)
            .Concat(Night(Day1.AddDays(1), 150, 49))
            .Concat(Night(Day1.AddDays(2), 146, 49))
            .Concat(Night(Day1.AddDays(3), 210, 95))
            .ToList();

        var summary = BaseLoad.Summarize(BaseLoad.Nights(buckets, [Meter, Andy], BuenosAires));

        Assert.Equal(210d, summary.LastNightWatts);
        Assert.Equal(148d, summary.TypicalWatts);
        Assert.Equal(95d, summary.DeviceLastNightWatts[Andy.Id]);
        Assert.Equal(49d, summary.DeviceTypicalWatts[Andy.Id]);
    }

    [Fact]
    public void Sin_noches_suficientes_no_hay_lo_tipico()
    {
        var buckets = Night(Day1, 148, 49).Concat(Night(Day1.AddDays(1), 210, 49)).ToList();

        var summary = BaseLoad.Summarize(BaseLoad.Nights(buckets, [Meter, Andy], BuenosAires));

        Assert.Equal(210d, summary.LastNightWatts);
        Assert.Null(summary.TypicalWatts);
    }

    [Fact]
    public void Un_enchufe_desactivado_no_entra()
    {
        var off = new Device
        {
            Id = Guid.NewGuid(), Name = "Viejo", Type = DeviceType.AthomPlugV3, Role = DeviceRole.Appliance, IsActive = false
        };
        var buckets = Night(Day1, 148, 49).Append(Hour(off, Day1, 4, 30)).ToList();

        var night = Assert.Single(BaseLoad.Nights(buckets, [Meter, Andy, off], BuenosAires));
        Assert.False(night.DeviceWatts.ContainsKey(off.Id));
    }

    [Fact]
    public void Sin_medidor_la_casa_es_la_suma_de_los_enchufes()
    {
        var pc = new Device { Id = Guid.NewGuid(), Name = "PC", Type = DeviceType.AthomPlugV3, Role = DeviceRole.Appliance };
        var buckets = Enumerable.Range(0, 8)
            .SelectMany(h => new[] { Hour(Andy, Day1, h, 49), Hour(pc, Day1, h, 32) })
            .ToList();

        var night = Assert.Single(BaseLoad.Nights(buckets, [Andy, pc], BuenosAires));
        Assert.Equal(81d, night.HouseWatts);
    }

    [Fact]
    public void Cuarenta_y_nueve_watts_todo_el_ciclo_son_unos_35_kWh()
    {
        Assert.Equal(35.28d, BaseLoad.CycleKwh(49), 2);
    }
}
