using EcoWattCasa.Application.Billing;
using EcoWattCasa.Application.Server;
using EcoWattCasa.Domain.Interfaces;
using EcoWattCasa.Infrastructure.Billing;
using EcoWattCasa.Infrastructure.Mqtt;
using EcoWattCasa.Infrastructure.Notifications;
using EcoWattCasa.Infrastructure.Persistence;
using EcoWattCasa.Infrastructure.Repositories;
using EcoWattCasa.Infrastructure.Server;
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
        services.AddScoped<IBillGoalRepository, BillGoalRepository>();
        services.AddScoped<IRelayCommandRepository, RelayCommandRepository>();
        services.AddSingleton<IBillTextExtractor, PdfBillTextExtractor>();

        services.Configure<RollupOptions>(configuration.GetSection(RollupOptions.SectionName));
        if (configuration.GetValue($"{RollupOptions.SectionName}:Enabled", true))
            services.AddHostedService<EnergyRollupService>();

        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.AddSingleton<MqttConnection>();
        services.AddScoped<IDeviceCommandPublisher, MqttCommandPublisher>();

        // El broker embebido se registra primero: los hosted services arrancan en orden y el
        // listener necesita que el puerto ya este escuchando cuando intente conectarse. Va
        // tambien como singleton para que el estado del servidor vea la misma instancia.
        if (configuration.GetValue($"{MqttOptions.SectionName}:Embedded", false))
        {
            services.AddSingleton<EmbeddedMqttBroker>();
            services.AddHostedService(sp => sp.GetRequiredService<EmbeddedMqttBroker>());
        }

        // Se puede apagar con "Mqtt:Enabled": false para levantar solo la API sin MQTT.
        if (configuration.GetValue($"{MqttOptions.SectionName}:Enabled", true))
            services.AddHostedService<MqttListenerService>();

        services.Configure<ServerReportOptions>(configuration.GetSection(ServerReportOptions.SectionName));
        services.AddSingleton<IHostMetricsReader, ProcHostMetricsReader>();
        services.AddSingleton<IHostReportReader, JsonHostReportReader>();
        services.AddScoped<IDatabaseStatsReader, PostgresDatabaseStatsReader>();
        services.AddSingleton<IMqttStatusReader>(sp => new MqttStatusReader(
            sp.GetRequiredService<MqttConnection>(),
            sp.GetService<EmbeddedMqttBroker>()));

        var notifications = configuration.GetSection(NotificationOptions.SectionName);
        services.Configure<NotificationOptions>(notifications);
        if (notifications.Get<NotificationOptions>()?.IsConfigured == true)
            services.AddHostedService<AlertNotifierService>();

        return services;
    }
}
