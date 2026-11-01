using EcoWattCasa.Application.Alerts;

namespace EcoWattCasa.Tests.Alerts;

/// <summary>Que se manda por Telegram: cada alerta una vez al aparecer, y otra al resolverse.</summary>
public class AlertNotificationTrackerTests
{
    private static Alert Crossing(string detail = "Al ritmo actual llegas el 20/10")
        => new("block-crossing", AlertSeverity.Warning, "Vas a cruzar los 150 kWh", detail);

    private static readonly Alert MeterSilent =
        new("meter-silent", AlertSeverity.Critical, "El medidor de tablero no reporta", "Hace 20 minutos");

    private static readonly Alert GoalUnreachable =
        new("goal-unreachable", AlertSeverity.Info, "El objetivo no se puede cumplir", "...");

    [Fact]
    public void Una_alerta_nueva_se_manda_una_sola_vez()
    {
        var tracker = new AlertNotificationTracker();

        Assert.Single(tracker.Next([Crossing()]).Raised);
        Assert.True(tracker.Next([Crossing()]).IsEmpty);
    }

    [Fact]
    public void Que_cambie_el_texto_no_es_una_alerta_nueva()
    {
        // La proyeccion se mueve cada pasada: avisar cada vez seria spam.
        var tracker = new AlertNotificationTracker();
        tracker.Next([Crossing("llegas el 20/10")]);

        Assert.True(tracker.Next([Crossing("llegas el 19/10")]).IsEmpty);
    }

    [Fact]
    public void Cuando_se_va_se_avisa_que_se_resolvio_y_si_vuelve_es_otro_aviso()
    {
        var tracker = new AlertNotificationTracker();
        tracker.Next([MeterSilent]);

        var resolved = tracker.Next([]);
        Assert.Equal("meter-silent", Assert.Single(resolved.Resolved).Code);

        Assert.Single(tracker.Next([MeterSilent]).Raised);
    }

    [Fact]
    public void Lo_informativo_se_queda_en_el_dashboard()
    {
        var tracker = new AlertNotificationTracker(AlertSeverity.Warning);

        var batch = tracker.Next([GoalUnreachable, MeterSilent]);

        Assert.Equal("meter-silent", Assert.Single(batch.Raised).Code);
    }

    [Fact]
    public void La_misma_regla_en_dos_dispositivos_son_dos_avisos()
    {
        var tracker = new AlertNotificationTracker();
        var pc = new Alert("device-silent", AlertSeverity.Warning, "PC no reporta", "...", Guid.NewGuid());
        var tv = new Alert("device-silent", AlertSeverity.Warning, "Tele no reporta", "...", Guid.NewGuid());

        Assert.Equal(2, tracker.Next([pc, tv]).Raised.Count);
    }

    [Fact]
    public void El_texto_lleva_la_gravedad_en_palabras()
    {
        Assert.StartsWith("[Critico] El medidor de tablero no reporta", AlertNotificationTracker.Format(MeterSilent));
    }
}
