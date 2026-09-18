using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Interfaces;
using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace EcoWattCasa.Tests.Devices;

/// <summary>Notificador que anota lo que se habria empujado al frontend.</summary>
internal sealed class FakeNotifier : IRealtimeNotifier
{
    public List<RelayStateDto> RelayStates { get; } = [];

    public Task ReadingReceivedAsync(EnergyReadingDto reading, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeviceRegisteredAsync(DeviceDto device, CancellationToken ct = default) => Task.CompletedTask;

    public Task RelayStateChangedAsync(RelayStateDto state, CancellationToken ct = default)
    {
        RelayStates.Add(state);
        return Task.CompletedTask;
    }
}

/// <summary>
/// El estado del rele que confirma el equipo en stat/{topic}/POWER.
///
/// Importa que sea el equipo el que manda y no lo que el backend pidio: si alguien aprieta el
/// boton fisico del enchufe, o si un comando se pierde, la pantalla tiene que mostrar la
/// realidad y no la intencion.
/// </summary>
public class RelayStateTests
{
    private static Device Plug() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Lavarropas",
        MqttTopic = "plug-lavarropas",
        Type = DeviceType.AthomPlugV3,
        Role = DeviceRole.Appliance,
        IsActive = true
    };

    private static (RelayStateService Service, FakeNotifier Notifier) Build(params Device[] devices)
    {
        var notifier = new FakeNotifier();
        var service = new RelayStateService(
            new FakeDeviceRepository(devices), notifier, NullLogger<RelayStateService>.Instance);

        return (service, notifier);
    }

    // ---------- Lo que publica Tasmota ----------

    [Theory]
    [InlineData("ON", true)]
    [InlineData("OFF", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("on", true)]
    [InlineData(" OFF ", false)]
    public void Se_aceptan_las_formas_en_que_Tasmota_informa_el_estado(string payload, bool expected)
    {
        // Segun el StateText configurado puede mandar ON/OFF, 1/0 o true/false.
        Assert.Equal(expected, RelayStateService.Parse(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOGGLE")]
    [InlineData(null)]
    public void Un_payload_que_no_es_un_estado_no_se_interpreta(string? payload)
    {
        Assert.Null(RelayStateService.Parse(payload));
    }

    // ---------- Lo que se guarda ----------

    [Fact]
    public async Task El_estado_confirmado_queda_guardado_en_el_dispositivo()
    {
        var plug = Plug();
        var (service, _) = Build(plug);

        await service.ApplyAsync("plug-lavarropas", "OFF");

        Assert.False(plug.RelayOn);
        Assert.NotNull(plug.RelayStateAt);
    }

    [Fact]
    public async Task Un_cambio_de_estado_se_empuja_al_frontend()
    {
        var plug = Plug();
        var (service, notifier) = Build(plug);

        await service.ApplyAsync("plug-lavarropas", "ON");

        var pushed = Assert.Single(notifier.RelayStates);
        Assert.Equal(plug.Id, pushed.DeviceId);
        Assert.True(pushed.On);
    }

    [Fact]
    public async Task Repetir_el_mismo_estado_no_genera_ruido()
    {
        // Tasmota republica el estado al arrancar y ante cada comando. Si cada repeticion
        // escribiera en la base y despertara al frontend, seria ruido puro.
        var plug = Plug();
        var (service, notifier) = Build(plug);

        await service.ApplyAsync("plug-lavarropas", "ON");
        var firstAt = plug.RelayStateAt;
        await service.ApplyAsync("plug-lavarropas", "ON");

        Assert.Single(notifier.RelayStates);
        Assert.Equal(firstAt, plug.RelayStateAt);
    }

    [Fact]
    public async Task Volver_a_cambiar_si_se_informa()
    {
        var plug = Plug();
        var (service, notifier) = Build(plug);

        await service.ApplyAsync("plug-lavarropas", "ON");
        await service.ApplyAsync("plug-lavarropas", "OFF");

        Assert.Equal(2, notifier.RelayStates.Count);
        Assert.False(plug.RelayOn);
    }

    [Fact]
    public async Task Un_estado_ilegible_no_toca_nada()
    {
        var plug = Plug();
        plug.RelayOn = true;
        var (service, notifier) = Build(plug);

        await service.ApplyAsync("plug-lavarropas", "QUIEN SABE");

        Assert.True(plug.RelayOn);
        Assert.Empty(notifier.RelayStates);
    }

    [Fact]
    public async Task Un_topic_desconocido_no_da_de_alta_un_dispositivo()
    {
        // A diferencia de la telemetria: un estado de rele no trae con que completar una ficha,
        // y el primer mensaje de SENSOR lo va a registrar igual.
        var (service, notifier) = Build();

        await service.ApplyAsync("plug-que-no-existe", "ON");

        Assert.Empty(notifier.RelayStates);
    }
}
