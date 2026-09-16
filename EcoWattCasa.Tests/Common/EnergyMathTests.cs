using EcoWattCasa.Application.Common;
using EcoWattCasa.Domain.ValueObjects;

namespace EcoWattCasa.Tests.Common;

/// <summary>
/// El paso de muestras crudas a kWh. Es el punto donde un contador que se reseteo puede
/// meter consumo inventado en la factura, asi que cada salvaguarda tiene su caso.
/// </summary>
public class EnergyMathTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    private static EnergySamples Samples(
        double? firstTotal,
        double? lastTotal,
        double avgWatts = 0,
        int sampleCount = 60,
        TimeSpan? span = null)
        => new(
            FirstTotalKwh: firstTotal,
            LastTotalKwh: lastTotal,
            AvgWatts: avgWatts,
            MaxWatts: avgWatts,
            SampleCount: sampleCount,
            FirstTimestamp: Start,
            LastTimestamp: Start + (span ?? TimeSpan.FromMinutes(59)));

    [Fact]
    public void Usa_el_delta_del_contador_cuando_es_monotono()
    {
        // 1,5 kWh de diferencia entre la primera y la ultima lectura del bucket.
        var samples = Samples(firstTotal: 100.0, lastTotal: 101.5, avgWatts: 1400);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: true, OneHour);

        Assert.Equal(1.5, kwh, precision: 6);
    }

    [Fact]
    public void Un_contador_reseteado_no_se_factura_como_consumo()
    {
        // El medidor volvio a cero a mitad del bucket: la ultima lectura es menor que la primera.
        // Sin esta guarda, MAX-MIN facturaria los 40 kWh del salto.
        var samples = Samples(firstTotal: 40.0, lastTotal: 0.3, avgWatts: 120);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: true, OneHour);

        // Cae a la integracion: 120 W durante 59 minutos.
        Assert.Equal(120d / 1000d * (59d / 60d), kwh, precision: 6);
    }

    [Fact]
    public void Un_delta_fisicamente_imposible_cae_a_la_integracion()
    {
        // 500 kWh en una hora son 500 kW: ninguna casa monofasica hace eso.
        var samples = Samples(firstTotal: 100.0, lastTotal: 600.0, avgWatts: 200);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: true, OneHour);

        Assert.Equal(200d / 1000d * (59d / 60d), kwh, precision: 6);
    }

    [Fact]
    public void El_techo_de_lo_plausible_deja_pasar_un_consumo_alto_pero_real()
    {
        // 25 kWh en una hora es muchisimo, pero cabe en 30 kW: se acepta.
        var samples = Samples(firstTotal: 100.0, lastTotal: 125.0, avgWatts: 25_000);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: true, OneHour);

        Assert.Equal(25.0, kwh, precision: 6);
    }

    [Fact]
    public void Sin_contador_acumulado_integra_la_potencia()
    {
        // El ESP32 con SCT-013 no lleva contador: solo potencia instantanea.
        var samples = Samples(firstTotal: null, lastTotal: null, avgWatts: 600);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: false, OneHour);

        Assert.Equal(600d / 1000d * (59d / 60d), kwh, precision: 6);
    }

    [Fact]
    public void Un_bucket_sin_muestras_no_consume_nada()
    {
        var samples = Samples(firstTotal: null, lastTotal: null, avgWatts: 0, sampleCount: 0);

        Assert.Equal(0d, EnergyMath.ToKwh(samples, preferCumulative: true, OneHour));
    }

    [Fact]
    public void Una_sola_muestra_vale_lo_que_dura_un_periodo_de_telemetria()
    {
        // Primera y ultima lectura en el mismo instante: el span es cero y hay que asumir algo.
        var samples = Samples(firstTotal: null, lastTotal: null, avgWatts: 3_600, sampleCount: 1, span: TimeSpan.Zero);

        var kwh = EnergyMath.ToKwh(samples, preferCumulative: false, OneHour);

        Assert.Equal(3_600d / 1000d * EnergyMath.AssumedTelemetryPeriod.TotalHours, kwh, precision: 8);
    }
}
