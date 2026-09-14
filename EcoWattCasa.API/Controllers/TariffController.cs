using EcoWattCasa.Application.Billing;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EcoWattCasa.API.Controllers;

[ApiController]
[Route("api/tariff")]
public sealed class TariffController(TariffService tariffs, BillImportService importer) : ControllerBase
{
    /// <summary>Cuadro tarifario vigente: cargo fijo diario, tramos, recargos y cargos del periodo.</summary>
    [HttpGet]
    public async Task<ActionResult<TariffScheduleDto>> GetCurrent(CancellationToken ct)
        => await tariffs.GetCurrentAsync(ct) is { } current
            ? Ok(current)
            : NotFound(new { error = "No hay ninguna tarifa cargada. Importa una factura o cargala a mano." });

    /// <summary>Serie completa de cuadros, del mas nuevo al mas viejo.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<TariffScheduleDto>>> GetHistory(CancellationToken ct)
        => Ok(await tariffs.GetHistoryAsync(ct));

    /// <summary>
    /// Carga un cuadro nuevo. No reescribe los anteriores: cada uno rige desde su fecha, asi
    /// los periodos ya costeados conservan su precio.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<TariffScheduleDto>> Upsert(UpsertTariffDto dto, CancellationToken ct)
        => Ok(await tariffs.UpsertAsync(dto, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await tariffs.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Facturas reales ya importadas.</summary>
    [HttpGet("bills")]
    public async Task<ActionResult<IReadOnlyList<ImportedBillDto>>> GetBills(CancellationToken ct)
        => Ok(await tariffs.GetBillsAsync(ct));

    [HttpDelete("bills/{id:guid}")]
    public async Task<IActionResult> DeleteBill(Guid id, CancellationToken ct)
        => await tariffs.DeleteBillAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Importa el PDF de la distribuidora: guarda el comprobante y deduce los cuadros
    /// tarifarios con su fecha de vigencia exacta. Devuelve el control de calidad: cuanto
    /// difiere el total recalculado del impreso.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<BillImportResultDto>> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No llego ningun archivo." });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "El archivo tiene que ser un PDF." });

        await using var stream = file.OpenReadStream();

        try
        {
            return Ok(await importer.ImportAsync(stream, ct));
        }
        catch (BillParseException ex)
        {
            // El PDF se abrio pero no tiene la forma esperada: es un 400, no un error del server.
            return BadRequest(new { error = $"No se pudo leer la factura: {ex.Message}" });
        }
    }
}
