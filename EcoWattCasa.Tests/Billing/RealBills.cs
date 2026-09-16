using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Tests.Billing;

/// <summary>
/// Las cinco facturas reales de la Cooperativa de Venado Tuerto (categoria Res Sin Sub VT)
/// que se usaron para modelar la tarifa. Son los casos de referencia: cualquier cambio en el
/// calculo tiene que seguir reproduciendo estos totales.
///
/// Todos los numeros salen del papel. Las fechas de vigencia de los precios se deducen del
/// reparto de dias que hace la propia factura: "26 Dias ... 04 Dias" sobre un periodo que
/// arranca el 05/03 significa que el precio nuevo rige desde el 31/03.
/// </summary>
public static class RealBills
{
    /// <param name="Period">Periodo que declara la factura. No coincide con el mes medido.</param>
    /// <param name="PrintedTotal">TOTAL A PAGAR impreso.</param>
    public sealed record Bill(
        string Period,
        string InvoiceNumber,
        DateOnly ReadingFrom,
        DateOnly ReadingTo,
        int Days,
        double Kwh,
        double MeterStart,
        double MeterEnd,
        decimal BasicAmount,
        decimal TotalTaxes,
        decimal PrintedTotal,
        IReadOnlyList<TariffSchedule> Schedules)
    {
        public override string ToString() => Period;

        /// <summary>Texto que PdfPig saca de este PDF, guardado como fixture.</summary>
        public string FixtureName => $"factura-{Period[3..]}-{Period[..2]}.txt";
    }

    /// <summary>Los cuatro recargos porcentuales dan exactamente 42% del importe basico.</summary>
    private const decimal Vat = 0.21m;
    private const decimal ProvincialLaw = 0.06m;
    private const decimal CapitalRemuneration = 0.0285m;
    private const decimal CapitalInvestment = 0.1215m;

    public static IEnumerable<object[]> All() => AllBills().Select(b => new object[] { b });

    public static IEnumerable<Bill> AllBills()
    {
        yield return May();
        yield return June();
        yield return July();
        yield return August();
        yield return September();
    }

    // Factura 05/2026 - dos periodos de precio (26 + 4 dias), aumento el 31/03.
    private static Bill May() => new(
        "05/2026",
        "0000-11303077",
        new DateOnly(2026, 3, 5),
        new DateOnly(2026, 4, 4),
        Days: 30,
        Kwh: 172,
        MeterStart: 27_165,
        MeterEnd: 27_337,
        BasicAmount: 43_541.83m,
        TotalTaxes: 24_299.07m,
        PrintedTotal: 67_840.90m,
        [
            Schedule(new DateOnly(2026, 3, 5), fixedPerDay: 104.0469m, 214.19m, 232.33m, 308.50m,
                publicLighting: 5_820.00m, renewables: 191.78m),
            Schedule(new DateOnly(2026, 3, 31), fixedPerDay: 109.7225m, 218.38m, 237.51m, 317.84m,
                publicLighting: 5_820.00m, renewables: 191.78m)
        ]);

    // Factura 06/2026 - dos periodos (26 + 4), aumento el 30/04. Sube el alumbrado publico.
    private static Bill June() => new(
        "06/2026",
        "0000-11339367",
        new DateOnly(2026, 4, 4),
        new DateOnly(2026, 5, 4),
        Days: 30,
        Kwh: 172,
        MeterStart: 27_337,
        MeterEnd: 27_509,
        BasicAmount: 44_913.23m,
        TotalTaxes: 25_888.35m,
        PrintedTotal: 70_801.58m,
        [
            Schedule(new DateOnly(2026, 4, 4), fixedPerDay: 109.7219m, 218.38m, 237.51m, 317.84m,
                publicLighting: 6_833.00m, renewables: 191.78m),
            Schedule(new DateOnly(2026, 4, 30), fixedPerDay: 114.96m, 235.47m, 255.51m, 339.67m,
                publicLighting: 6_833.00m, renewables: 191.78m)
        ]);

    // Factura 07/2026 - 29 dias repartidos 27 + 2, aumento el 31/05.
    private static Bill July() => new(
        "07/2026",
        "0000-11387364",
        new DateOnly(2026, 5, 4),
        new DateOnly(2026, 6, 2),
        Days: 29,
        Kwh: 187,
        MeterStart: 27_509,
        MeterEnd: 27_696,
        BasicAmount: 52_795.57m,
        TotalTaxes: 29_384.61m,
        PrintedTotal: 82_180.18m,
        [
            Schedule(new DateOnly(2026, 5, 4), fixedPerDay: 114.9593m, 235.47m, 255.51m, 339.67m,
                publicLighting: 6_979.00m, renewables: 230.81m),
            Schedule(new DateOnly(2026, 5, 31), fixedPerDay: 120.20m, 238.64m, 259.59m, 347.58m,
                publicLighting: 6_979.00m, renewables: 230.81m)
        ]);

    // Factura 08/2026 - 30 dias repartidos 28 + 2, aumento el 30/06.
    private static Bill August() => new(
        "08/2026",
        "0000-11425520",
        new DateOnly(2026, 6, 2),
        new DateOnly(2026, 7, 2),
        Days: 30,
        Kwh: 187,
        MeterStart: 27_696,
        MeterEnd: 27_883,
        BasicAmount: 53_903.40m,
        TotalTaxes: 29_848.72m,
        PrintedTotal: 83_752.12m,
        [
            Schedule(new DateOnly(2026, 6, 2), fixedPerDay: 120.1982m, 238.64m, 259.59m, 347.58m,
                publicLighting: 6_979.00m, renewables: 230.81m),
            Schedule(new DateOnly(2026, 6, 30), fixedPerDay: 122.035m, 243.73m, 265.01m, 354.34m,
                publicLighting: 6_979.00m, renewables: 230.81m)
        ]);

    // Factura 09/2026 - un solo periodo de precio, 29 dias.
    private static Bill September() => new(
        "09/2026",
        "0000-11467758",
        new DateOnly(2026, 7, 2),
        new DateOnly(2026, 7, 31),
        Days: 29,
        Kwh: 175,
        MeterStart: 27_883,
        MeterEnd: 28_058,
        BasicAmount: 50_553.01m,
        TotalTaxes: 28_442.12m,
        PrintedTotal: 78_995.13m,
        [
            Schedule(new DateOnly(2026, 7, 2), fixedPerDay: 122.0348m, 243.73m, 265.01m, 354.34m,
                publicLighting: 6_979.00m, renewables: 230.81m)
        ]);

    /// <summary>Cuadro con la estructura de la cooperativa: 3 tramos, 4 recargos y 2 cargos fijos.</summary>
    public static TariffSchedule Schedule(
        DateOnly validFrom,
        decimal fixedPerDay,
        decimal block1,
        decimal block2,
        decimal block3,
        decimal publicLighting,
        decimal renewables)
    {
        var schedule = new TariffSchedule
        {
            Id = Guid.NewGuid(),
            ValidFrom = validFrom,
            FixedChargePerDay = fixedPerDay,
            Source = "test",
            CreatedAt = DateTimeOffset.UnixEpoch
        };

        schedule.Blocks.Add(new TariffBlock { Order = 1, Label = "Hasta 75 kWh", UpToKwh = 75, PricePerKwh = block1 });
        schedule.Blocks.Add(new TariffBlock { Order = 2, Label = "Hasta 150 kWh", UpToKwh = 150, PricePerKwh = block2 });
        schedule.Blocks.Add(new TariffBlock { Order = 3, Label = "Hasta 300 kWh", UpToKwh = 300, PricePerKwh = block3 });

        schedule.Surcharges.Add(new TariffSurcharge { Name = "IVA 21% Energia", Rate = Vat });
        schedule.Surcharges.Add(new TariffSurcharge { Name = "Ley Pcial. 10014", Rate = ProvincialLaw });
        schedule.Surcharges.Add(new TariffSurcharge { Name = "Cap.Rem.L.B.T.", Rate = CapitalRemuneration });
        schedule.Surcharges.Add(new TariffSurcharge { Name = "Cap.Inv.Bienes de Uso", Rate = CapitalInvestment });

        schedule.PeriodCharges.Add(new TariffPeriodCharge { Name = "Tasa de Alum. Pub.", Amount = publicLighting });
        schedule.PeriodCharges.Add(new TariffPeriodCharge { Name = "Ley Pcial. 12692", Amount = renewables });

        return schedule;
    }
}
