using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Tests.Devices;

/// <summary>
/// Borrar el historial de consumo sin llevarse los dispositivos.
///
/// Existe para el dia que llega el hardware real: la base va a tener meses de consumo simulado
/// sobre los mismos dispositivos. Borrar el dispositivo entero no sirve, porque se llevaria el
/// topic, el canal y el bloqueo del rele, que es justo lo que hay que conservar.
/// </summary>
public class HistoryPurgeTests
{
    private static Device Plug() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Lavarropas",
        MqttTopic = "plug-lavarropas",
        Type = DeviceType.AthomPlugV3,
        Role = DeviceRole.Appliance,
        RelayLocked = true,
        MinRelayIntervalSeconds = 600,
        ChannelIndex = 0,
        IsActive = true
    };

    private static (DeviceService Service, FakeDeviceRepository Devices, EmptyReadingRepository Readings) Build(
        params Device[] devices)
    {
        var deviceRepo = new FakeDeviceRepository(devices);
        var readings = new EmptyReadingRepository();
        var service = new DeviceService(
            deviceRepo, readings, new FakeRelayCommandRepository(), new FakeCommandPublisher());

        return (service, deviceRepo, readings);
    }

    [Fact]
    public async Task Se_puede_borrar_el_historial_de_un_solo_dispositivo()
    {
        var plug = Plug();
        var (service, _, readings) = Build(plug);

        var result = await service.DeleteHistoryAsync(plug.Id);

        Assert.NotNull(result);
        Assert.Equal(plug.Id, Assert.Single(readings.Purged));
    }

    [Fact]
    public async Task Sin_dispositivo_se_borra_el_historial_de_toda_la_casa()
    {
        var (service, _, readings) = Build(Plug());

        var result = await service.DeleteHistoryAsync(deviceId: null);

        Assert.NotNull(result);
        Assert.Null(Assert.Single(readings.Purged));
        Assert.Contains("todos", result.Scope);
    }

    [Fact]
    public async Task El_dispositivo_y_su_configuracion_sobreviven_al_borrado()
    {
        // El punto entero: el topic, el canal y el bloqueo del rele hay que volver a cargarlos
        // a mano si se pierden, y son lo unico que no se puede recuperar solo.
        var plug = Plug();
        var (service, repo, _) = Build(plug);

        await service.DeleteHistoryAsync(plug.Id);

        var survivor = await repo.GetByIdAsync(plug.Id);
        Assert.NotNull(survivor);
        Assert.Equal("plug-lavarropas", survivor.MqttTopic);
        Assert.True(survivor.RelayLocked);
        Assert.Equal(600, survivor.MinRelayIntervalSeconds);
    }

    [Fact]
    public async Task Un_dispositivo_que_no_existe_no_borra_nada()
    {
        // Si devolviera "listo" sin borrar, quien lo pidio creeria que el historial se fue.
        var (service, _, readings) = Build(Plug());

        var result = await service.DeleteHistoryAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Empty(readings.Purged);
    }

    [Fact]
    public async Task El_resultado_dice_sobre_que_se_borro()
    {
        var plug = Plug();
        var (service, _, _) = Build(plug);

        var result = await service.DeleteHistoryAsync(plug.Id);

        Assert.Equal("Lavarropas", result!.Scope);
    }
}
