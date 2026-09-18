using EcoWattCasa.Application.Common;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Tests.Energy;

/// <summary>
/// El reparto entre lo que se mide enchufe por enchufe y lo que no.
///
/// El error que estos tests existen para evitar es el doble conteo: el medidor de tablero mide
/// la acometida, asi que lo que consume la heladera ya esta dentro de su lectura. Sumar el
/// medidor mas los enchufes infla el total de la casa y con el, la factura estimada.
/// </summary>
public class HouseConsumptionTests
{
    private static Device Meter(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Medidor de tablero",
        Type = DeviceType.AthomEm2,
        Role = DeviceRole.HouseMeter,
        IsActive = active
    };

    private static Device Plug(string name, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = DeviceType.AthomPlugV3,
        Role = DeviceRole.Appliance,
        IsActive = active
    };

    // ---------- Con medidor de tablero ----------

    [Fact]
    public void El_total_de_la_casa_lo_da_el_medidor_no_la_suma()
    {
        // 12 kWh de tablero con 7 repartidos en enchufes: la casa consumio 12, no 19.
        var meter = Meter();
        var pc = Plug("PC");
        var fridge = Plug("Heladera");

        var split = HouseConsumption.Split(
            [meter, pc, fridge],
            new Dictionary<Guid, double> { [meter.Id] = 12d, [pc.Id] = 4d, [fridge.Id] = 3d });

        Assert.Equal(12d, split.HouseKwh);
        Assert.Equal(7d, split.MeasuredKwh);
        Assert.True(split.HasMeter);
    }

    [Fact]
    public void Lo_no_identificado_es_lo_que_no_paso_por_ningun_enchufe()
    {
        var meter = Meter();
        var pc = Plug("PC");

        var split = HouseConsumption.Split(
            [meter, pc],
            new Dictionary<Guid, double> { [meter.Id] = 10d, [pc.Id] = 4d });

        Assert.Equal(6d, split.UnidentifiedKwh);
    }

    [Fact]
    public void Un_enchufe_sin_lecturas_no_resta_nada()
    {
        var meter = Meter();
        var idle = Plug("Enchufe libre");

        var split = HouseConsumption.Split([meter, idle], new Dictionary<Guid, double> { [meter.Id] = 10d });

        Assert.Equal(0d, split.MeasuredKwh);
        Assert.Equal(10d, split.UnidentifiedKwh);
    }

    [Fact]
    public void Con_todo_medido_no_queda_consumo_sin_identificar()
    {
        var meter = Meter();
        var pc = Plug("PC");

        var split = HouseConsumption.Split(
            [meter, pc],
            new Dictionary<Guid, double> { [meter.Id] = 4d, [pc.Id] = 4d });

        Assert.Equal(0d, split.UnidentifiedKwh);
        Assert.False(split.MeasuredExceedsHouse);
    }

    // ---------- Sin medidor: como funcionaba antes de instalar el EM2 ----------

    [Fact]
    public void Sin_medidor_la_casa_es_la_suma_de_los_enchufes()
    {
        // El sistema tiene que servir antes de poner el EM2 en el tablero. Mide de menos,
        // pero mide.
        var pc = Plug("PC");
        var fridge = Plug("Heladera");

        var split = HouseConsumption.Split(
            [pc, fridge],
            new Dictionary<Guid, double> { [pc.Id] = 4d, [fridge.Id] = 3d });

        Assert.Equal(7d, split.HouseKwh);
        Assert.False(split.HasMeter);
    }

    [Fact]
    public void Sin_medidor_no_se_inventa_consumo_no_identificado()
    {
        // No hay con que compararlo: informar un numero ahi seria inventarlo.
        var pc = Plug("PC");

        var split = HouseConsumption.Split([pc], new Dictionary<Guid, double> { [pc.Id] = 4d });

        Assert.Equal(0d, split.UnidentifiedKwh);
    }

    [Fact]
    public void Un_medidor_inactivo_no_cuenta_como_medidor()
    {
        // Es como no tenerlo: el total vuelve a ser la suma de los enchufes.
        var meter = Meter(active: false);
        var pc = Plug("PC");

        var split = HouseConsumption.Split(
            [meter, pc],
            new Dictionary<Guid, double> { [meter.Id] = 10d, [pc.Id] = 4d });

        Assert.False(split.HasMeter);
        Assert.Equal(4d, split.HouseKwh);
    }

    [Fact]
    public void Un_enchufe_inactivo_no_suma_al_desglose()
    {
        var meter = Meter();
        var retired = Plug("Enchufe guardado", active: false);

        var split = HouseConsumption.Split(
            [meter, retired],
            new Dictionary<Guid, double> { [meter.Id] = 10d, [retired.Id] = 4d });

        Assert.Equal(0d, split.MeasuredKwh);
        Assert.Equal(10d, split.UnidentifiedKwh);
    }

    // ---------- Cuando los numeros no cierran ----------

    [Fact]
    public void Si_los_enchufes_superan_al_tablero_se_avisa()
    {
        // Fisicamente imposible: o un enchufe cuelga de un circuito que el medidor no ve, o
        // hay un ChannelIndex mal puesto. Esconderlo detras de un cero oculta el problema.
        var meter = Meter();
        var pc = Plug("PC");

        var split = HouseConsumption.Split(
            [meter, pc],
            new Dictionary<Guid, double> { [meter.Id] = 3d, [pc.Id] = 5d });

        Assert.True(split.MeasuredExceedsHouse);
        Assert.Equal(0d, split.UnidentifiedKwh);
    }

    [Fact]
    public void Una_diferencia_de_redondeo_no_se_reporta_como_error()
    {
        // Dos contadores distintos leidos en momentos distintos nunca cierran al ultimo decimal.
        var meter = Meter();
        var pc = Plug("PC");

        var split = HouseConsumption.Split(
            [meter, pc],
            new Dictionary<Guid, double> { [meter.Id] = 5d, [pc.Id] = 5.0005d });

        Assert.False(split.MeasuredExceedsHouse);
        Assert.Equal(0d, split.UnidentifiedKwh);
    }

    [Fact]
    public void Dos_medidores_se_suman()
    {
        // Una casa trifasica llevaria un medidor por fase y cada uno ve una parte.
        var a = Meter();
        var b = Meter();

        var split = HouseConsumption.Split(
            [a, b],
            new Dictionary<Guid, double> { [a.Id] = 6d, [b.Id] = 4d });

        Assert.Equal(10d, split.HouseKwh);
    }

    // ---------- La serie temporal de la casa ----------

    [Fact]
    public void La_serie_de_la_casa_sale_solo_del_medidor()
    {
        var meter = Meter();
        var pc = Plug("PC");

        var series = HouseConsumption.HouseSeriesDevices([meter, pc]);

        Assert.Equal([meter.Id], series);
    }

    [Fact]
    public void Sin_medidor_la_serie_la_arman_los_enchufes()
    {
        var pc = Plug("PC");
        var fridge = Plug("Heladera");

        var series = HouseConsumption.HouseSeriesDevices([pc, fridge]);

        Assert.Equal(2, series.Count);
    }

    [Fact]
    public void Un_enchufe_inactivo_tampoco_aparece_en_el_desglose()
    {
        // Tiene que coincidir con lo que hace Split: si uno le diera fila y el otro no lo
        // contara como medido, sus kWh saldrian dos veces, en su fila y en lo no identificado.
        var meter = Meter();
        var retired = Plug("Enchufe guardado", active: false);

        Assert.Empty(HouseConsumption.Appliances([meter, retired]));
    }

    [Fact]
    public void El_medidor_no_aparece_en_el_desglose_por_aparato()
    {
        // Si apareciera, seria una tarjeta con el consumo de toda la casa al lado de las de
        // cada aparato, y el porcentaje de cada uno daria la mitad.
        var meter = Meter();
        var pc = Plug("PC");

        var appliances = HouseConsumption.Appliances([meter, pc]).ToList();

        Assert.Equal([pc], appliances);
    }
}
