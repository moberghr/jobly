using Jobly.Core.Enums;
using Jobly.Core.Handlers;

namespace Jobly.Core.Helper;

public class JobParameters
{
    internal string? Message { get; set; }

    internal string? Type { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public string? Queue { get; set; }

    public Guid? ParentId { get; set; }

    public State? State { get; set; }

    public string? Mutex { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    public JobParameters Configure<T>(Action<T> configure)
        where T : class, IJobMetadata
    {
        Metadata ??= new Dictionary<string, object>();
        var typed = MetadataFactory.Create<T>(Metadata);
        configure(typed);
        Metadata = (Dictionary<string, object>)(object)typed;

        return this;
    }
}
