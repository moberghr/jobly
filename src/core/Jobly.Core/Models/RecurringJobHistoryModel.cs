using Jobly.Core.Enums;

namespace Jobly.Core.Models;

public class RecurringJobHistoryModel
{
    public Guid JobId { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool JobExists { get; set; }

    public string? Type { get; set; }

    public State? CurrentState { get; set; }
}
