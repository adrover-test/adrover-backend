extern alias Identity;

using Adrover.Data.Context;
using Adrover.Functions.Configuration;
using Adrover.Functions.Intelisa;
using Adrover.Functions.Services;
using ManagedIdentityCredential = Identity::Azure.Identity.ManagedIdentityCredential;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, configuration) =>
    {
        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        services.AddDbContext<AdRoverDbContext>(options => options.UseSqlServer(connectionString));
        services.Configure<IntelisaOptions>(configuration.GetSection("Intelisa"));
        services.Configure<SchedulingOptions>(configuration.GetSection("Scheduling"));
        services.Configure<ServiceBusOptions>(configuration.GetSection("ServiceBus"));
        services.AddHttpClient<IntelisaClient>(client => client.Timeout = TimeSpan.FromMinutes(9));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceBusOptions>>().Value;
            var connection = configuration["ServiceBusConnection"] ?? settings.ConnectionString ?? configuration["ServiceBus:ConnectionString"];
            var namespaceName = configuration["ServiceBusConnection:fullyQualifiedNamespace"] ?? settings.FullyQualifiedNamespace;
            return !string.IsNullOrWhiteSpace(connection)
                ? new ServiceBusClient(connection)
                : !string.IsNullOrWhiteSpace(namespaceName)
                    ? new ServiceBusClient(namespaceName, new ManagedIdentityCredential())
                    : throw new InvalidOperationException("Set ServiceBus:ConnectionString or ServiceBus:FullyQualifiedNamespace.");
        });
        services.AddSingleton(sp =>
        {
            var blobs = configuration.GetSection("AzureBlob");
            var connection = blobs["ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connection))
                return new BlobServiceClient(connection);
            var serviceUri = blobs["ServiceUri"];
            return !string.IsNullOrWhiteSpace(serviceUri)
                ? new BlobServiceClient(new Uri(serviceUri), new ManagedIdentityCredential())
                : throw new InvalidOperationException("Set AzureBlob:ConnectionString or AzureBlob:ServiceUri.");
        });
        services.AddScoped<ContentOutboxPublisher>();
        services.AddScoped<ContentProcessor>();
        services.AddScoped<DailyScheduler>();
    })
    .Build();

await host.RunAsync();
