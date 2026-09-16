using EcoWattCasa.Application.Alerts;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(AlertService alerts) : ControllerBase
{
    /// <summary>
    /// Alertas vigentes ahora mismo: cruce de tramo proyectado, dispositivos mudos y
    /// proyeccion por encima del ciclo anterior. Se calculan al pedirlas, no se guardan.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Alert>>> GetActive(CancellationToken ct)
        => Ok(await alerts.GetActiveAsync(ct));
}
