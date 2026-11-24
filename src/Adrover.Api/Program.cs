using Serilog;
using Adrover.Api.Configuration;
using Adrover.Api.Middleware;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var configuredJwtKey = builder.Configuration["Jwt:SecretKey"];
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(configuredJwtKey) ||
     System.Text.Encoding.UTF8.GetByteCount(configuredJwtKey) < 32 ||
     configuredJwtKey.StartsWith("adrover-super-secret-key", StringComparison.Ordinal)))
    throw new InvalidOperationException("Configure a unique, secure JWT signing key outside development.");

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
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });
    // Add services
    builder.Services.AddControllers();
    builder.Services.AddApplicationServices(builder.Configuration);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("client-follow-up", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        options.AddPolicy("driver-login", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });
    
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Configure middleware
    app.UseExceptionHandling();
    
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();
    }
    app.UseCors("AllowAll");

    app.UseApplicationMiddleware(app.Environment);
    app.UseRateLimiter();
    
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
