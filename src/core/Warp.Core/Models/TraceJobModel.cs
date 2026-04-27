using Warp.Core.Enums;

namespace Warp.Core.Models;

public class TraceJobModel
{
    public Guid Id { get; set; }

    public JobKind Kind { get; set; }

    public string? Type { get; set; }

    public string? HandlerType { get; set; }

    public State CurrentState { get; set; }

    public Guid? ParentJobId { get; set; }

    public Guid? SpawnedByJobId { get; set; }

    public DateTime CreateTime { get; set; }
}
