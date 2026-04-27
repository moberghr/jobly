using Warp.Core.Handlers;

namespace Warp.Core.NoRestart;

public partial interface ICanBeRestartedMetadata : IJobMetadata
{
    bool? CanBeRestarted { get; set; }
}
