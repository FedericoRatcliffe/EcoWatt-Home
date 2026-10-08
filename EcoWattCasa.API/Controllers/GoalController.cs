using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/goal")]
public sealed class GoalController(GoalService goals) : ControllerBase
{
    /// <summary>
    /// Objetivo de factura por ciclo, con los kWh que permite y el minimo que sale la factura
    /// del ciclo en curso sin consumir nada. Sin objetivo fijado, el monto viene null.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<BillGoalDto>> Get(CancellationToken ct)
        => Ok(await goals.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<BillGoalDto>> Set(SetBillGoalDto dto, CancellationToken ct)
        => Ok(await goals.SetAsync(dto, ct));

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
        => await goals.ClearAsync(ct) ? NoContent() : NotFound();
}
