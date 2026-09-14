using EcoWattCasa.Application.Billing;
using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EcoWattCasa.Application;

public static class DependencyInjection
{
    /// <summary>Registra los casos de uso. Los repositorios los aporta Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, string? timeZoneId = null)
    {
        var timeZone = new HomeTimeZone(timeZoneId);
        services.AddSingleton(timeZone);
        services.AddSingleton(new BillingCycles(timeZone));

        services.AddScoped<DeviceService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<TariffService>();
        services.AddScoped<EnergyIngestionService>();
        services.AddScoped<BillImportService>();

        return services;
    }
}
