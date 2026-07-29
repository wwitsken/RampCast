using Anthropic;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RampCast.Functions;
using RampCast.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateOnStart();

// Resolved eagerly rather than via DI: AddAzureClients below needs the value
// before the container is built. ValidateOnStart above still guards anyone who
// injects IOptions<StorageOptions> later, and is the reason this check can't
// just be skipped in favor of that — a container that fails to build here would
// otherwise throw from deep inside AddBlobServiceClient with the same kind of
// unhelpful stack trace this whole options setup exists to avoid.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? new StorageOptions();
var storageValidation = new StorageOptionsValidator().Validate(null, storageOptions);
if (storageValidation.Failed)
    throw new InvalidOperationException(storageValidation.FailureMessage);

builder.Services.AddAzureClients(clientBuilder =>
{
    if (storageOptions.ConnectionString is { } connectionString)
    {
        clientBuilder.AddBlobServiceClient(connectionString);
        clientBuilder.AddQueueServiceClient(connectionString);
        clientBuilder.AddTableServiceClient(connectionString);
    }
    else
    {
        // The Uri overload is required (not the connection-string one) for
        // UseCredential below to actually apply to these clients.
        clientBuilder.AddBlobServiceClient(storageOptions.BlobServiceUri);
        clientBuilder.AddQueueServiceClient(storageOptions.QueueServiceUri);
        clientBuilder.AddTableServiceClient(storageOptions.TableServiceUri);
        clientBuilder.UseCredential(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
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
