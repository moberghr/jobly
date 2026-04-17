using Jobly.Core.Helper;

namespace Jobly.Core.NoRestart;

public static class NoRestartExtensions
{
    public static JobParameters WithRestart(this JobParameters parameters, bool canBeRestarted)
    {
        parameters.Configure<ICanBeRestartedMetadata>(x => x.CanBeRestarted = canBeRestarted);

        return parameters;
    }
}
