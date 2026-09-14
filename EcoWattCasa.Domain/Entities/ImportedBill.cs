namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Una factura real ya emitida, cargada desde el PDF de la distribuidora. Sirve para dos
/// cosas: saber las fechas reales del ciclo de lectura (que se corren mes a mes) y poder
/// comparar lo que estimo el sistema contra lo que efectivamente vino facturado.
/// </summary>
public class ImportedBill
{
    public Guid Id { get; set; }

    /// <summary>Numero de comprobante, ej. "0000-11467758".</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Periodo que declara la factura, ej. "09/2026". No coincide con el mes medido.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Fecha de la lectura anterior del medidor.</summary>
    public DateOnly ReadingFrom { get; set; }

    /// <summary>Fecha de la lectura actual del medidor.</summary>
    public DateOnly ReadingTo { get; set; }

    public int Days { get; set; }
    public double Kwh { get; set; }

    public double MeterStart { get; set; }
    public double MeterEnd { get; set; }

    public decimal BasicAmount { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal Total { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Precio medio efectivo del kWh de esta factura, impuestos incluidos.</summary>
    public decimal AveragePricePerKwh => Kwh > 0 ? Math.Round(Total / (decimal)Kwh, 2) : 0m;
}
