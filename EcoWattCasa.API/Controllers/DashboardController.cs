using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard, HomeTimeZone tz) : ControllerBase
{
    /// <summary>
    /// Consumo y costo del dia por dispositivo. El costo del dia sale de costear el ciclo
    /// entero y ver que tramo le toco a ese dia, porque los kWh del final del ciclo caen en
    /// el tramo caro.
    /// </summary>
    [HttpGet("daily")]
    public async Task<ActionResult<DailyDashboardDto>> GetDaily([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await dashboard.GetDailyAsync(date ?? tz.Today, ct));

    /// <summary>
    /// La factura del periodo reconstruida, con desglose por dispositivo.
    /// Por defecto usa el ciclo de facturacion real (el que marca la ultima factura importada);
    /// con cycle=false devuelve el mes calendario.
    /// </summary>
    [HttpGet("period")]
    public async Task<ActionResult<PeriodDashboardDto>> GetPeriod(
        [FromQuery] bool cycle = true,
        [FromQuery] int offset = 0,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        if (offset is < -60 or > 0)
            return BadRequest(new { error = "'offset' tiene que estar entre -60 y 0." });

        if (month is < 1 or > 12)
            return BadRequest(new { error = "'month' tiene que estar entre 1 y 12." });

        if (year is < 2000 or > 2999)
            return BadRequest(new { error = "'year' fuera de rango." });

        return Ok(await dashboard.GetPeriodAsync(cycle, offset, year, month, ct));
    }

    /// <summary>Donde esta anclado el ciclo de facturacion y de que dato salio.</summary>
    [HttpGet("cycle")]
    public async Task<ActionResult<object>> GetCycle(CancellationToken ct)
    {
        var (anchor, fromBill) = await dashboard.GetCycleAnchorAsync(ct);
        return Ok(new
        {
            anchor,
            cycleDays = BillingCycles.NominalCycleDays,
            fromBill,
            source = fromBill
                ? "fecha de lectura de la ultima factura importada"
                : "supuesto por defecto: todavia no hay facturas importadas"
        });
    }
}
