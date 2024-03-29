using Microsoft.Extensions.Options;

namespace Jobly.Worker;

public static class ConfigurationHelper
{
    public static JoblyWorkerConfiguration ConfigureDefault(
        this IConfigureOptions<JoblyWorkerConfiguration> configuration)
    {
        var options = new JoblyWorkerConfiguration();
        configuration.Configure(options);
        return options;
    }
}