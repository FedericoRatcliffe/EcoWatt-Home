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

        services.Configure<RollupOptions>(configuration.GetSection(RollupOptions.SectionName));
        if (configuration.GetValue($"{RollupOptions.SectionName}:Enabled", true))
            services.AddHostedService<EnergyRollupService>();

        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.AddSingleton<MqttConnection>();
        services.AddScoped<IDeviceCommandPublisher, MqttCommandPublisher>();

        // El broker embebido se registra primero: los hosted services arrancan en orden y el
        // listener necesita que el puerto ya este escuchando cuando intente conectarse.
        if (configuration.GetValue($"{MqttOptions.SectionName}:Embedded", false))
            services.AddHostedService<EmbeddedMqttBroker>();

        // Se puede apagar con "Mqtt:Enabled": false para levantar solo la API sin MQTT.
        if (configuration.GetValue($"{MqttOptions.SectionName}:Enabled", true))
            services.AddHostedService<MqttListenerService>();

        return services;
    }
}
