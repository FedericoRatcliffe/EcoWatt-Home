using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Server;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/server")]
public sealed class ServerController(ServerStatusService server) : ControllerBase
{
    /// <summary>
    /// El estado del servidor: CPU, memoria, disco, contenedores, SMART, backups, base y broker,
    /// con los problemas ya evaluados. Solo lectura; la pantalla lo pide cada pocos segundos.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusDto>> GetStatus(CancellationToken ct)
        => Ok(await server.GetStatusAsync(ct));
}
