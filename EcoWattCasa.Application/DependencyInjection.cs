using EcoWattCasa.Application.Alerts;
using EcoWattCasa.Application.Billing;
using EcoWattCasa.Application.Common;
using EcoWattCasa.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EcoWattCasa.Application;

public static class DependencyInjection
{
    /// <summary>Registra los casos de uso. Los repositorios los aporta Infrastructure.</summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        string? timeZoneId = null,
        IConfiguration? configuration = null)
    {
        var timeZone = new HomeTimeZone(timeZoneId);
        services.AddSingleton(timeZone);
        services.AddSingleton(new BillingCycles(timeZone));

        services.AddScoped<DeviceService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<TariffService>();
        services.AddScoped<EnergyIngestionService>();
        services.AddScoped<RelayStateService>();
        services.AddScoped<BillImportService>();
        services.AddScoped<AlertService>();

        if (configuration is not null)
            services.Configure<AlertThresholds>(configuration.GetSection(AlertThresholds.SectionName));
        else
            services.Configure<AlertThresholds>(_ => { });

        return services;
    }
}
