namespace EcoWattCasa.Domain.Entities;

/// <summary>
/// Cuanto se quiere pagar de luz por ciclo de facturacion. Hay uno solo para la casa: no tiene
/// vigencia ni historia, porque lo que importa es contra que se mide el ciclo en curso.
///
/// Se guarda en pesos y no en kWh porque es como se piensa la factura. La traduccion a kWh
/// depende de la tarifa del ciclo (tramos, cargos fijos, recargos), asi que se hace al pedirla
/// y sigue sola a cada cuadro tarifario nuevo.
/// </summary>
public class BillGoal
{
    public Guid Id { get; set; }

    /// <summary>Total de la factura que no se quiere superar, impuestos incluidos.</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
