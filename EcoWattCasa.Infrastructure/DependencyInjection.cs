using EcoWattCasa.Application.Billing;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Billing;
using EcoWattCasa.Infrastructure.Mqtt;
using EcoWattCasa.Infrastructure.Persistence;
using EcoWattCasa.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EcoWattCasa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Postgres en la configuracion.");

        services.AddDbContext<EcoWattDbContext>(options => options
            .UseNpgsql(connectionString)
            // Las columnas quedan en snake_case sin escribir HasColumnName en cada propiedad.
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IEnergyReadingRepository, EnergyReadingRepository>();
        services.AddScoped<ITariffRepository, TariffRepository>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddSingleton<IBillTextExtractor, PdfBillTextExtractor>();

        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.AddSingleton<MqttConnection>();
        services.AddScoped<IDeviceCommandPublisher, MqttCommandPublisher>();

        // Se puede apagar con "Mqtt:Enabled": false para levantar solo la API sin broker.
        if (configuration.GetValue($"{MqttOptions.SectionName}:Enabled", true))
            services.AddHostedService<MqttListenerService>();

        return services;
    }
}
