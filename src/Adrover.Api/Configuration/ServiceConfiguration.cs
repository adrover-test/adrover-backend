using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Serilog;
using Adrover.Data.Context;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Adrover.Application.DTOs;
using Adrover.Application.Validators;
using Adrover.Application.Services;

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

        services.AddAuthorization();

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

        var razorpaySettings = configuration.GetSection("Razorpay");
        var httpClient = new HttpClient();
        var logger = LoggerFactory.Create(builder => builder.AddSerilog()).CreateLogger<RazorpayPaymentService>();
        services.AddSingleton<IPaymentGatewayService>(new RazorpayPaymentService(
            razorpaySettings["KeyId"] ?? string.Empty,
            razorpaySettings["KeySecret"] ?? string.Empty,
            httpClient,
            logger
        ));

        var azureSettings = configuration.GetSection("AzureBlob");
        services.AddSingleton<IFileStorageService>(new AzureBlobStorageService(
            azureSettings["ConnectionString"] ?? string.Empty
        ));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdvertisementRepository, AdvertisementRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        // Application Services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdvertisementService, AdvertisementService>();

        // Validation
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

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
