using Adrover.Application.Services;
using Adrover.Application.Validators;
using Adrover.Data.Context;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

namespace Adrover.Api.Configuration;

/// <summary>
/// Dependency injection and service configuration
/// </summary>
public static class ServiceConfiguration
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<AdRoverDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        // JWT Configuration
        var jwtSettings = configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        var issuer = jwtSettings["Issuer"] ?? "adrover.api";
        var audience = jwtSettings["Audience"] ?? "adrover.clients";
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "15");

        services.AddSingleton<ITokenService>(new TokenService(secretKey, issuer, audience, expirationMinutes));

        // Authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));
        });

        // OTP (MSG91)
        services.AddMemoryCache();
        services.AddHttpClient("notifications", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Adrover.Backend.Services.OtpService>();
        services.AddScoped<Adrover.Backend.Services.InquiryService>();
        services.AddScoped<Adrover.Backend.Services.IntelisaAuthService>();

        // Infrastructure Services
        services.AddSingleton<IPasswordService, PasswordService>();

        var smtpSettings = configuration.GetSection("Smtp");
        services.AddSingleton<IEmailService>(new EmailService(
            smtpSettings["Host"] ?? "smtp.gmail.com",
            int.Parse(smtpSettings["Port"] ?? "587"),
            smtpSettings["Username"] ?? string.Empty,
            smtpSettings["Password"] ?? string.Empty,
            smtpSettings["FromEmail"] ?? string.Empty,
            smtpSettings["FromName"] ?? "Adrover"
        ));

        services.AddHttpClient<IPaymentGatewayService, RazorpayPaymentService>();

        var azureSettings = configuration.GetSection("AzureBlob");
        services.AddSingleton<IFileStorageService>(new AzureBlobStorageService(
            azureSettings["ConnectionString"] ?? string.Empty
        ));

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdvertisementRepository, AdvertisementRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        // Application Services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdvertisementService, AdvertisementService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<INotificationService, NotificationService>();

        // CORS
        var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static IApplicationBuilder UseApplicationMiddleware(
        this IApplicationBuilder app,
        IWebHostEnvironment env)
    {
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
