using Jobly.Core.Handlers;

namespace Jobly.Core.NoRestart;

public partial interface ICanBeRestartedMetadata : IJobMetadata
{
    bool? CanBeRestarted { get; set; }
}
