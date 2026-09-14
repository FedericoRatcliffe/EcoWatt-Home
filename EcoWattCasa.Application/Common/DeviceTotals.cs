namespace EcoWattCasa.Application.Common;

/// <summary>Acumulador mutable usado al armar los totales del dashboard.</summary>
internal sealed class DeviceTotals
{
    public double Kwh { get; set; }
    public decimal Cost { get; set; }
}
