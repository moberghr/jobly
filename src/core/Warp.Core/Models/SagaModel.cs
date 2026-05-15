namespace Warp.Core.Models;

public class SagaListItemModel
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string CorrelationKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public class SagaDetailModel
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string CorrelationKey { get; set; } = string.Empty;

    public string StateJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Version { get; set; }
}

public class SagaActivityEntryModel
{
    public Guid JobId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string JobState { get; set; } = string.Empty;

    public DateTime CreateTime { get; set; }

    public List<JobLogModel> Logs { get; set; } = [];
}

public class SagaActivityResponseModel
{
    public List<SagaActivityEntryModel> Entries { get; set; } = [];

    /// <summary>Total number of <c>SagaJobLink</c> rows for this saga, regardless of cap.</summary>
    public long TotalInvocations { get; set; }

    /// <summary>True when <see cref="Entries"/> was truncated to the cap (currently 200).</summary>
    public bool IsTruncated { get; set; }
}

public class SagaStatsModel
{
    public long LiveSagas { get; set; }

    public long StartedToday { get; set; }

    public long CompletedToday { get; set; }
}
