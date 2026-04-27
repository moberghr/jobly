using Warp.Core.Helper;

namespace Warp.Core.NoRestart;

public static class NoRestartExtensions
{
    public static JobParameters WithRestart(this JobParameters parameters, bool canBeRestarted)
    {
        parameters.Configure<ICanBeRestartedMetadata>(x => x.CanBeRestarted = canBeRestarted);

        return parameters;
    }
}
