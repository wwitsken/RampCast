using Anthropic;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RampCast.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient(builder.Configuration.GetValue<string>("AzureBlobUri"));
    clientBuilder.AddQueueServiceClient(builder.Configuration.GetValue<string>("AzureQueueUri"));
    clientBuilder.AddTableServiceClient(builder.Configuration.GetValue<string>("AzureTableUri"));

    if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
    {
        // Managed identity token credential discovered when running in Azure environments
        ManagedIdentityCredential credential = new(ManagedIdentityId.SystemAssigned);
        clientBuilder.UseCredential(credential);
    }
});

// Anthropic client — registered once and constructor-injected, mirroring the
// Azure client registration above. AddAzureClients only knows Azure client types,
// so this uses AddSingleton. The SDK reads ANTHROPIC_API_KEY from the environment
// when no explicit key is configured.
builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["ANTHROPIC_API_KEY"];
    return string.IsNullOrEmpty(apiKey)
        ? new AnthropicClient()
        : new AnthropicClient { ApiKey = apiKey };
});

builder.Services.AddSingleton<SchemaValidator>();
builder.Services.AddSingleton<StaffingPlanGenerator>();
builder.Services.AddSingleton<PlanDocumentStore>();
builder.Services.AddSingleton<BatchStatusStore>();
builder.Services.AddSingleton<AuthTokenStore>();
builder.Services.AddSingleton<AccessTokenService>();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
