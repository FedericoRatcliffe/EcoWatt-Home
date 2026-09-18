using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using EcoWattCasa.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController(DeviceService devices, DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> GetAll(CancellationToken ct)
        => Ok(await devices.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceDto>> GetById(Guid id, CancellationToken ct)
        => await devices.GetByIdAsync(id, ct) is { } device ? Ok(device) : NotFound();

    [HttpPost]
    public async Task<ActionResult<DeviceDto>> Create(CreateDeviceDto dto, CancellationToken ct)
    {
        var created = await devices.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeviceDto>> Update(Guid id, UpdateDeviceDto dto, CancellationToken ct)
        => await devices.UpdateAsync(id, dto, ct) is { } updated ? Ok(updated) : NotFound();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await devices.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Lecturas crudas de un rango. Sin parametros devuelve las ultimas 24 h.</summary>
    [HttpGet("{id:guid}/readings")]
    public async Task<ActionResult<IReadOnlyList<EnergyReadingDto>>> GetReadings(
        Guid id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int maxPoints = 2000,
        CancellationToken ct = default)
    {
        var toUtc = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var fromUtc = (from ?? toUtc.AddHours(-24)).ToUniversalTime();

        if (fromUtc >= toUtc)
            return BadRequest(new { error = "'from' tiene que ser anterior a 'to'." });

        return Ok(await devices.GetReadingsAsync(id, fromUtc, toUtc, maxPoints, ct));
    }

    /// <summary>
    /// Historial agregado con costo, para los botones de 24 h / 7 d / 30 d de la vista de detalle.
    /// Hasta 48 h se agrupa por hora; mas alla, por dia.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<DeviceHistoryDto>> GetHistory(
        Guid id,
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        if (hours is < 1 or > 24 * 400)
            return BadRequest(new { error = "'hours' tiene que estar entre 1 y 9600." });

        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.AddHours(-hours);
        var granularity = hours <= 48 ? BucketGranularity.Hour : BucketGranularity.Day;

        var history = await dashboard.GetDeviceHistoryAsync(id, fromUtc, toUtc, granularity, ct);
        return history is null ? NotFound() : Ok(history);
    }

    /// <summary>Prende o apaga el enchufe publicando cmnd/{topic}/POWER.</summary>
    [HttpPost("{id:guid}/power")]
    public async Task<IActionResult> SetPower(Guid id, SetPowerDto dto, CancellationToken ct)
    {
        try
        {
            var result = await devices.SetPowerAsync(id, dto.On, RelayCommandSource.Dashboard, ct);

            if (!result.DeviceExists)
                return NotFound();

            // 409 y no 400: el pedido esta bien formado, pero el estado del dispositivo no
            // admite conmutarlo ahora. El motivo va en texto porque se muestra tal cual.
            return result.Allowed
                ? Accepted(new { deviceId = id, requested = dto.On ? "ON" : "OFF" })
                : Conflict(new { error = result.Reason, outcome = result.Outcome.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            // Broker caido: 503, no 500. El pedido es valido, el transporte no esta.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Borra el historial de consumo de un dispositivo, dejando su configuracion intacta.
    /// Para el dia que llega el hardware real y hay que sacarse de encima el consumo simulado.
    /// </summary>
    [HttpDelete("{id:guid}/history")]
    public async Task<IActionResult> DeleteHistory(Guid id, [FromQuery] string? confirm, CancellationToken ct)
        => await PurgeAsync(id, confirm, ct);

    /// <summary>Borra el historial de consumo de toda la casa.</summary>
    [HttpDelete("history")]
    public async Task<IActionResult> DeleteAllHistory([FromQuery] string? confirm, CancellationToken ct)
        => await PurgeAsync(null, confirm, ct);

    /// <summary>
    /// Es irreversible y la app no tiene login, asi que se pide la palabra explicita: evita
    /// que un curl de mas o un link guardado borre meses de mediciones.
    /// </summary>
    private async Task<IActionResult> PurgeAsync(Guid? id, string? confirm, CancellationToken ct)
    {
        if (!string.Equals(confirm, "borrar", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Borra el historial de forma irreversible. Repeti el pedido con ?confirm=borrar." });

        var result = await devices.DeleteHistoryAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Historial de conmutaciones del rele, incluidas las rechazadas.</summary>
    [HttpGet("{id:guid}/relay-history")]
    public async Task<ActionResult<IReadOnlyList<RelayCommandDto>>> GetRelayHistory(
        Guid id, [FromQuery] int limit, CancellationToken ct)
        => Ok(await devices.GetRelayHistoryAsync(id, limit <= 0 ? 20 : limit, ct));
}

public sealed record SetPowerDto(bool On);
