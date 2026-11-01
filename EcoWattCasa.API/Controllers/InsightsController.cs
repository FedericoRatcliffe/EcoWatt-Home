using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/insights")]
public sealed class InsightsController(InsightsService insights) : ControllerBase
{
    /// <summary>
    /// Lo que gasta la casa sin que nadie use nada: la hora mas baja de cada madrugada, resumida
    /// con la mediana de las ultimas noches.
    /// </summary>
    [HttpGet("base-load")]
    public async Task<ActionResult<BaseLoadDto>> GetBaseLoad([FromQuery] int nights = 14, CancellationToken ct = default)
    {
        if (nights is < 1 or > 90)
            return BadRequest(new { error = "'nights' tiene que estar entre 1 y 90." });

        return Ok(await insights.GetBaseLoadAsync(nights, ct));
    }

    /// <summary>Cuanto cambia la factura del ciclo si se deja de consumir (o se suma) una potencia.</summary>
    [HttpPost("simulate")]
    public async Task<ActionResult<SimulationDto>> Simulate(SimulationRequestDto request, CancellationToken ct)
    {
        if (request.FromHour is < 0 or > 23 || request.ToHour is < 0 or > 23)
            return BadRequest(new { error = "Las horas van de 0 a 23." });

        if (Math.Abs(request.Watts) > 20_000)
            return BadRequest(new { error = "'watts' fuera de rango." });

        if (request.BaselineKwh is < 0)
            return BadRequest(new { error = "'baselineKwh' no puede ser negativo." });

        return Ok(await insights.SimulateAsync(request, ct));
    }

    /// <summary>Cada factura importada contra lo que midio el tablero en las mismas fechas.</summary>
    [HttpGet("bill-checks")]
    public async Task<ActionResult<IReadOnlyList<BillCheckDto>>> GetBillChecks(CancellationToken ct)
        => Ok(await insights.GetBillChecksAsync(ct));

    /// <summary>La semana tipica de la casa, hora por hora.</summary>
    [HttpGet("weekly-profile")]
    public async Task<ActionResult<WeeklyProfileDto>> GetWeeklyProfile([FromQuery] int weeks = 4, CancellationToken ct = default)
    {
        if (weeks is < 1 or > 26)
            return BadRequest(new { error = "'weeks' tiene que estar entre 1 y 26." });

        return Ok(await insights.GetWeeklyProfileAsync(weeks, ct));
    }
}
