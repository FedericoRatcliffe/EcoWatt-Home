using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Tests.Devices;

/// <summary>
/// El camino completo de un ON/OFF: guarda, publicacion y auditoria.
///
/// <see cref="RelayGuardTests"/> cubre la regla pura. Aca se prueba lo que la rodea, que es
/// donde estan los errores caros: que un comando rechazado no llegue igual al broker, y que
/// todo intento quede registrado aunque no se ejecute.
/// </summary>
public class RelayCommandTests
{
    private static Device Plug(string name = "Lavarropas", bool locked = false, int minInterval = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MqttTopic = "plug-lavarropas",
        Type = DeviceType.AthomPlugV3,
        Role = DeviceRole.Appliance,
        RelayLocked = locked,
        MinRelayIntervalSeconds = minInterval,
        IsActive = true
    };

    private static Device Meter() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Medidor de tablero",
        MqttTopic = "em2-tablero",
        Type = DeviceType.AthomEm2,
        Role = DeviceRole.HouseMeter,
        IsActive = true
    };

    private sealed record Harness(
        DeviceService Service,
        FakeCommandPublisher Publisher,
        FakeRelayCommandRepository History);

    private static Harness Build(params Device[] devices)
    {
        var publisher = new FakeCommandPublisher();
        var history = new FakeRelayCommandRepository();
        var service = new DeviceService(
            new FakeDeviceRepository(devices), new EmptyReadingRepository(), history, publisher);

        return new Harness(service, publisher, history);
    }

    // ---------- Lo que si se ejecuta ----------

    [Fact]
    public async Task Un_enchufe_libre_se_apaga_y_el_comando_sale_al_broker()
    {
        var plug = Plug();
        var h = Build(plug);

        var result = await h.Service.SetPowerAsync(plug.Id, on: false);

        Assert.True(result.Allowed);
        Assert.Equal(("plug-lavarropas", false), h.Publisher.Published.Single());
    }

    [Fact]
    public async Task Lo_que_se_ejecuta_queda_registrado_como_enviado()
    {
        var plug = Plug();
        var h = Build(plug);

        await h.Service.SetPowerAsync(plug.Id, on: true);

        var audit = h.History.Commands.Single();
        Assert.Equal(RelayCommandOutcome.Sent, audit.Outcome);
        Assert.True(audit.RequestedOn);
        Assert.Equal(RelayCommandSource.Dashboard, audit.Source);
        Assert.Null(audit.Reason);
    }

    [Fact]
    public async Task Un_dispositivo_que_no_existe_se_distingue_de_uno_rechazado()
    {
        // El controlador necesita separarlos: uno es 404 y el otro 409.
        var h = Build();

        var result = await h.Service.SetPowerAsync(Guid.NewGuid(), on: true);

        Assert.False(result.DeviceExists);
        Assert.Empty(h.History.Commands);
    }

    // ---------- Lo que la guarda frena ----------

    [Fact]
    public async Task La_heladera_bloqueada_no_llega_al_broker()
    {
        // El punto de todo el mecanismo: un error del dashboard no puede cortarle la comida.
        var fridge = Plug("Heladera", locked: true);
        var h = Build(fridge);

        var result = await h.Service.SetPowerAsync(fridge.Id, on: false);

        Assert.False(result.Allowed);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Un_rechazo_tambien_queda_registrado_y_con_motivo()
    {
        // Sin esto, un ON/OFF que no salio no deja rastro de quien lo pidio ni de por que.
        var fridge = Plug("Heladera", locked: true);
        var h = Build(fridge);

        await h.Service.SetPowerAsync(fridge.Id, on: false);

        var audit = h.History.Commands.Single();
        Assert.Equal(RelayCommandOutcome.BlockedLocked, audit.Outcome);
        Assert.False(audit.WasSent);
        Assert.Contains("bloqueado", audit.Reason);
    }

    [Fact]
    public async Task El_medidor_de_tablero_rechaza_el_comando_por_no_tener_rele()
    {
        var meter = Meter();
        var h = Build(meter);

        var result = await h.Service.SetPowerAsync(meter.Id, on: false);

        Assert.Equal(RelayCommandOutcome.BlockedNoRelay, result.Outcome);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task El_segundo_clic_seguido_se_frena_por_la_ventana_de_tiempo()
    {
        var plug = Plug(minInterval: 600);
        var h = Build(plug);

        await h.Service.SetPowerAsync(plug.Id, on: false);
        var second = await h.Service.SetPowerAsync(plug.Id, on: true);

        Assert.Equal(RelayCommandOutcome.BlockedTooSoon, second.Outcome);
        Assert.Single(h.Publisher.Published);
        Assert.Contains("Faltan", second.Reason);
    }

    [Fact]
    public async Task Un_rechazo_no_extiende_la_ventana_de_tiempo()
    {
        // La ventana se mide contra el ultimo comando que movio el rele. Si un rechazo la
        // corriera, insistir con el boton dejaria el rele trabado para siempre.
        var plug = Plug(minInterval: 600);
        var h = Build(plug);

        await h.Service.SetPowerAsync(plug.Id, on: false);
        var blocked = await h.Service.SetPowerAsync(plug.Id, on: true);
        var blockedAgain = await h.Service.SetPowerAsync(plug.Id, on: true);

        // Los dos rechazos reportan la misma espera restante, no una que se renueva sola.
        Assert.Equal(blocked.Reason, blockedAgain.Reason);
    }

    // ---------- Cuando falla el transporte ----------

    [Fact]
    public async Task Si_el_broker_esta_caido_el_error_se_propaga()
    {
        // El controlador lo traduce a 503: el pedido es valido, el transporte no esta.
        var plug = Plug();
        var h = Build(plug);
        h.Publisher.ThrowOnPublish = new InvalidOperationException("Sin conexion al broker MQTT.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.SetPowerAsync(plug.Id, on: true));
    }

    [Fact]
    public async Task Un_fallo_del_broker_queda_en_el_historial()
    {
        var plug = Plug();
        var h = Build(plug);
        h.Publisher.ThrowOnPublish = new InvalidOperationException("Sin conexion al broker MQTT.");

        try
        {
            await h.Service.SetPowerAsync(plug.Id, on: true);
        }
        catch (InvalidOperationException)
        {
            // Se ignora: lo que se prueba es que quedo registrado antes de propagarse.
        }

        var audit = h.History.Commands.Single();
        Assert.Equal(RelayCommandOutcome.Failed, audit.Outcome);
        Assert.Contains("broker", audit.Reason);
    }

    [Fact]
    public async Task Un_intento_fallido_no_cuenta_como_ultima_conmutacion()
    {
        // Si contara, un broker caido dejaria el rele en cuarentena diez minutos sin haberlo
        // movido nunca.
        var plug = Plug(minInterval: 600);
        var h = Build(plug);
        h.Publisher.ThrowOnPublish = new InvalidOperationException("Sin conexion al broker MQTT.");

        try
        {
            await h.Service.SetPowerAsync(plug.Id, on: true);
        }
        catch (InvalidOperationException)
        {
        }

        h.Publisher.ThrowOnPublish = null;
        var retry = await h.Service.SetPowerAsync(plug.Id, on: true);

        Assert.True(retry.Allowed);
    }

    // ---------- El historial que se muestra ----------

    [Fact]
    public async Task El_historial_trae_lo_ejecutado_y_lo_rechazado()
    {
        var fridge = Plug("Heladera", locked: true);
        var plug = Plug();
        var h = Build(fridge, plug);

        await h.Service.SetPowerAsync(plug.Id, on: false);
        await h.Service.SetPowerAsync(fridge.Id, on: false);

        var all = await h.Service.GetRelayHistoryAsync(deviceId: null);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c is { WasSent: true, DeviceName: "Lavarropas" });
        Assert.Contains(all, c => c is { WasSent: false, DeviceName: "Heladera" });
    }

    [Fact]
    public async Task El_historial_se_puede_filtrar_por_dispositivo()
    {
        var fridge = Plug("Heladera", locked: true);
        var plug = Plug();
        var h = Build(fridge, plug);

        await h.Service.SetPowerAsync(plug.Id, on: false);
        await h.Service.SetPowerAsync(fridge.Id, on: false);

        var onlyFridge = await h.Service.GetRelayHistoryAsync(fridge.Id);

        Assert.Equal("Heladera", Assert.Single(onlyFridge).DeviceName);
    }
}
