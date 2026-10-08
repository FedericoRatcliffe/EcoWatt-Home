using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.DTOs;
using EcoWattCasa.Domain.Interfaces;

namespace EcoWattCasa.Application.Services;

/// <summary>
/// El objetivo de factura de la casa. Ademas del monto devuelve lo que significa en el ciclo
/// en curso, para que al fijarlo se vea si alcanza: un objetivo por debajo de los cargos fijos
/// no se cumple aunque se corte la luz.
/// </summary>
public sealed class GoalService(
    IBillGoalRepository goals,
    ITariffRepository tariffs,
    DashboardService dashboard,
    HomeTimeZone tz,
    BillingCycles cycles)
{
    public async Task<BillGoalDto> GetAsync(CancellationToken ct = default)
    {
        var goal = await goals.GetAsync(ct);

        var history = await tariffs.GetHistoryAsync(ct);
        if (history.Count == 0)
            return new BillGoalDto(goal?.Amount, goal?.UpdatedAt, null, null);

        var (anchor, _) = await dashboard.GetCycleAnchorAsync(ct);
        var cycle = cycles.CycleContaining(tz.Today, anchor);

        var minimum = BillCalculator.Build(cycle.From, cycle.To, 0d, history).Total;
        double? budget = goal is null ? null : GoalPlanner.KwhBudget(goal.Amount, cycle.From, cycle.To, history);

        return new BillGoalDto(goal?.Amount, goal?.UpdatedAt, minimum, budget);
    }

    public async Task<BillGoalDto> SetAsync(SetBillGoalDto dto, CancellationToken ct = default)
    {
        if (dto.Amount <= 0m)
            throw new DeviceValidationException("El objetivo tiene que ser un monto mayor a cero.");

        await goals.SetAsync(Math.Round(dto.Amount, 2), ct);
        return await GetAsync(ct);
    }

    public Task<bool> ClearAsync(CancellationToken ct = default) => goals.ClearAsync(ct);
}
