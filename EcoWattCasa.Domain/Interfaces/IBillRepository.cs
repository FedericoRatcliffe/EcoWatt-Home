using EcoWattCasa.Domain.Entities;

namespace EcoWattCasa.Domain.Interfaces;

public interface IBillRepository
{
    Task<IReadOnlyList<ImportedBill>> GetAllAsync(CancellationToken ct = default);

    /// <summary>La factura mas reciente por fecha de lectura: de ahi sale el ciclo real.</summary>
    Task<ImportedBill?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Guarda la factura, o la reemplaza si ya se importo ese comprobante.</summary>
    Task<ImportedBill> UpsertAsync(ImportedBill bill, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
