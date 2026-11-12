using Serilog;
using Adrover.Api.Configuration;
using Adrover.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(Directory.GetCurrentDirectory(), "logs", "adrover-.txt"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    // Add services
    builder.Services.AddControllers();
    builder.Services.AddApplicationServices(builder.Configuration);
    
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Configure middleware
    app.UseExceptionHandling();
    
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseApplicationMiddleware(app.Environment);
    
    app.MapControllers();
    
    // Health check endpoints
    app.MapGet("/health", () => new { status = "Healthy", timestamp = DateTime.UtcNow, version = "1.0.0" });
    app.MapGet("/health/database", async (Adrover.Data.Context.AdRoverDbContext db) =>
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await db.Database.CanConnectAsync();
            sw.Stop();
            
            return Results.Ok(new { status = "Connected", responseTime = sw.ElapsedMilliseconds, lastChecked = DateTime.UtcNow });
        }
        catch
        {
            return Results.StatusCode(503);
        }
    });

    Log.Information("Starting Adrover.Api");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
