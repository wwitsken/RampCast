using Microsoft.Extensions.Configuration;

namespace RampCast.Functions;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Reads a required configuration value, throwing a clear startup error (naming the
    /// missing key and where to set it) instead of letting a null propagate into an
    /// unrelated SDK constructor and surface as a cryptic ArgumentNullException.
    /// </summary>
    public static string GetRequiredValue(this IConfiguration configuration, string key)
    {
        var value = configuration.GetValue<string>(key);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Required configuration value '{key}' is missing or empty. Set it in local.settings.json " +
                "for local development, or as an Application Setting on the Function App in Azure.");
        }

        return value;
    }
}
