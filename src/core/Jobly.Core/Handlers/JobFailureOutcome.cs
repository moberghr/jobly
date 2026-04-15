using Jobly.Core.Enums;

namespace Jobly.Core.Handlers;

public class JobFailureOutcome
{
    public State State { get; init; }

    public DateTime? ScheduleTime { get; init; }

    public bool ClearHandlerType { get; init; }
}
