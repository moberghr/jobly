using Warp.Core.Enums;
using Warp.Core.Handlers;

namespace Warp.Core.Helper;

public class JobParameters
{
    internal string? Message { get; set; }

    internal string? Type { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public string? Queue { get; set; }

    public Guid? ParentId { get; set; }

    public State? State { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    public JobParameters Configure<T>(Action<T> configure)
        where T : class, IJobMetadata
    {
        Metadata ??= [];
        var typed = MetadataFactory.Create<T>(Metadata);
        configure(typed);
        Metadata = (Dictionary<string, object>)(object)typed;

        return this;
    }
}
