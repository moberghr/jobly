using Microsoft.Extensions.Options;

namespace Jobly.Core.Helper;

public static class ConfigurationHelper
{
    public static JoblyConfiguration ConfigureDefault(this IConfigureOptions<JoblyConfiguration> configuration)
    {
        var options = new JoblyConfiguration();
        configuration.Configure(options);
        return options;
    }
}