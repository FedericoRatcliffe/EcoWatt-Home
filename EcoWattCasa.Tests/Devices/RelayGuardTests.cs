using EcoWattCasa.Domain.Entities;
using EcoWattCasa.Domain.Enums;

namespace EcoWattCasa.Tests.Devices;

/// <summary>
/// La guarda del rele. Se aplica a todos los origenes de comando, no solo a una automatizacion
/// futura: un clic repetido en el dashboard pasa por las mismas reglas.
/// </summary>
public class RelayGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 18, 0, 0, TimeSpan.Zero);

    private static Device Plug(bool locked = false, int minInterval = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Lavarropas",
        MqttTopic = "plug-lavarropas",
        Type = DeviceType.AthomPlugV3,
        Role = DeviceRole.Appliance,
        RelayLocked = locked,
        MinRelayIntervalSeconds = minInterval
    };

    private static Device Meter() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Medidor de tablero",
        MqttTopic = "em2-tablero",
        Type = DeviceType.AthomEm2,
        Role = DeviceRole.HouseMeter
    };

    private static RelayCommand SentAt(DateTimeOffset when) => new()
    {
        RequestedOn = true,
        Source = RelayCommandSource.Dashboard,
        Outcome = RelayCommandOutcome.Sent,
        CreatedAt = when
    };

    // ---------- Capacidades del hardware ----------

    [Fact]
    public void Un_enchufe_libre_puede_conmutarse()
    {
        var decision = RelayGuard.Evaluate(Plug(), lastSent: null, Now);

        Assert.True(decision.Allowed);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void El_medidor_de_tablero_no_tiene_rele()
    {
        // El EM2 es solo medicion: no hay nada que conmutar.
        var decision = RelayGuard.Evaluate(Meter(), lastSent: null, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(RelayCommandOutcome.BlockedNoRelay, decision.RejectedAs);
        Assert.Contains("no tiene rele", decision.Reason);
    }

    [Fact]
    public void El_medidor_nunca_declara_que_puede_conmutar()
    {
        Assert.False(Meter().HasRelay);
        Assert.False(Meter().CanToggleRelay);
    }

    // ---------- Bloqueo explicito ----------

    [Fact]
    public void Un_dispositivo_bloqueado_no_se_puede_apagar()
    {
        // Es el caso de la heladera: un corte por error arruina la comida.
        var decision = RelayGuard.Evaluate(Plug(locked: true), lastSent: null, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(RelayCommandOutcome.BlockedLocked, decision.RejectedAs);
        Assert.Contains("bloqueado", decision.Reason);
    }

    [Fact]
    public void El_bloqueo_pesa_mas_que_la_ventana_de_tiempo()
    {
        // Bloqueado y recien conmutado: el motivo que se reporta es el bloqueo, que es el
        // permanente, no el temporal.
        var decision = RelayGuard.Evaluate(
            Plug(locked: true, minInterval: 600), SentAt(Now.AddSeconds(-5)), Now);

        Assert.Equal(RelayCommandOutcome.BlockedLocked, decision.RejectedAs);
    }

    [Fact]
    public void Un_dispositivo_bloqueado_no_declara_que_puede_conmutar()
    {
        Assert.True(Plug(locked: true).HasRelay);
        Assert.False(Plug(locked: true).CanToggleRelay);
    }

    // ---------- Tiempo minimo entre conmutaciones ----------

    [Fact]
    public void Sin_intervalo_configurado_se_puede_conmutar_de_inmediato()
    {
        var decision = RelayGuard.Evaluate(Plug(minInterval: 0), SentAt(Now.AddSeconds(-1)), Now);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Antes_del_intervalo_minimo_el_comando_se_rechaza()
    {
        // Protege al compresor de la heladera del ciclado corto.
        var decision = RelayGuard.Evaluate(Plug(minInterval: 600), SentAt(Now.AddSeconds(-120)), Now);

        Assert.False(decision.Allowed);
        Assert.Equal(RelayCommandOutcome.BlockedTooSoon, decision.RejectedAs);
    }

    [Fact]
    public void El_rechazo_dice_cuanto_falta()
    {
        var decision = RelayGuard.Evaluate(Plug(minInterval: 600), SentAt(Now.AddSeconds(-120)), Now);

        Assert.Contains("Faltan 480 s", decision.Reason);
    }

    [Fact]
    public void Cumplido_el_intervalo_se_habilita()
    {
        var decision = RelayGuard.Evaluate(Plug(minInterval: 600), SentAt(Now.AddSeconds(-600)), Now);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Sin_comandos_previos_no_hay_ventana_que_esperar()
    {
        var decision = RelayGuard.Evaluate(Plug(minInterval: 600), lastSent: null, Now);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Un_comando_con_fecha_futura_no_bloquea_para_siempre()
    {
        // Si el reloj se corrio hacia atras, el ultimo comando puede quedar "en el futuro".
        // Eso no debe dejar el rele trabado indefinidamente.
        var decision = RelayGuard.Evaluate(Plug(minInterval: 600), SentAt(Now.AddHours(1)), Now);

        Assert.True(decision.Allowed);
    }

    // ---------- El estado que se registra ----------

    [Fact]
    public void Un_comando_publicado_se_marca_como_enviado()
    {
        Assert.True(SentAt(Now).WasSent);
    }

    [Theory]
    [InlineData(RelayCommandOutcome.BlockedLocked)]
    [InlineData(RelayCommandOutcome.BlockedTooSoon)]
    [InlineData(RelayCommandOutcome.BlockedNoRelay)]
    [InlineData(RelayCommandOutcome.Failed)]
    public void Un_comando_que_no_salio_no_cuenta_como_enviado(RelayCommandOutcome outcome)
    {
        var command = SentAt(Now);
        command.Outcome = outcome;

        Assert.False(command.WasSent);
    }
}
