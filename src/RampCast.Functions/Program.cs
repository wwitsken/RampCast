using Anthropic;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RampCast.Functions;
using RampCast.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddAzureClients(clientBuilder =>
{
    string?[] identityUris =
    [
        builder.Configuration.GetValue<string>("AzureBlobUri"),
        builder.Configuration.GetValue<string>("AzureQueueUri"),
        builder.Configuration.GetValue<string>("AzureTableUri"),
    ];
    var identityUriCount = identityUris.Count(uri => !string.IsNullOrEmpty(uri));

    if (identityUriCount == identityUris.Length)
    {
        // Prefer managed identity: Azure*Uri settings present means these are real
        // service endpoints, not connection strings, so the Uri overload must be used
        // for UseCredential to actually apply — the string overload binds to the
        // connection-string constructor instead and ignores any registered credential.
        clientBuilder.AddBlobServiceClient(new Uri(builder.Configuration.GetRequiredValue("AzureBlobUri")));
        clientBuilder.AddQueueServiceClient(new Uri(builder.Configuration.GetRequiredValue("AzureQueueUri")));
        clientBuilder.AddTableServiceClient(new Uri(builder.Configuration.GetRequiredValue("AzureTableUri")));
        clientBuilder.UseCredential(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }
    else if (identityUriCount == 0)
    {
        // Fall back to a connection string (local dev via Azurite, or any environment
        // without the identity URIs configured).
        var connectionString = builder.Configuration.GetRequiredValue("AzureWebJobsStorage");
        clientBuilder.AddBlobServiceClient(connectionString);
        clientBuilder.AddQueueServiceClient(connectionString);
        clientBuilder.AddTableServiceClient(connectionString);
    }
    else
    {
        throw new InvalidOperationException(
            "Partial Azure storage identity configuration: AzureBlobUri, AzureQueueUri, and AzureTableUri " +
            "must all be set together to use managed identity, or all left unset to fall back to the " +
            "AzureWebJobsStorage connection string.");
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
