using System.Text.Json.Serialization;
using EcoWattCasa.API.Hubs;
using EcoWattCasa.API.Infrastructure;
using EcoWattCasa.Application;
using EcoWattCasa.Application.Interfaces;
using EcoWattCasa.Infrastructure;
using EcoWattCasa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "ecowatt-frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Los enums viajan como texto ("SonoffPowR2") para que el front no dependa de indices.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    // SignalR con WebSockets necesita credenciales permitidas para el handshake.
    .AllowCredentials()));

builder.Services.AddApplication(builder.Configuration["EcoWatt:TimeZone"]);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();

var app = builder.Build();

app.UseExceptionHandler();

// Migracion y seed al arrancar: es una app local de una sola casa, no hay pipeline de deploy.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EcoWattDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbSeeder.MigrateAndSeedAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "No se pudo migrar la base. Revisa que PostgreSQL este levantado (docker compose up postgres).");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors(CorsPolicy);
app.MapControllers();
app.MapHub<EnergyHub>(EnergyHub.Route);
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

app.Run();
