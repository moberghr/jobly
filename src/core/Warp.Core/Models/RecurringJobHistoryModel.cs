using Warp.Core.Enums;

namespace Warp.Core.Models;

public class RecurringJobHistoryModel
{
    public Guid? JobId { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool JobExists { get; set; }

    public string? Type { get; set; }

    public State? CurrentState { get; set; }

    public bool Skipped { get; set; }
}
