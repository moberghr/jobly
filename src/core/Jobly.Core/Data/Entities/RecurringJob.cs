using System.ComponentModel.DataAnnotations;
using Jobly.Core.Interfaces;

namespace Jobly.Core.Data.Entities;

public class RecurringJob : IConcurrencyToken
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Message { get; set; }

    public string? Cron { get; set; }

    public string Queue { get; set; } = "default";

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? NextExecution { get; set; }

    public DateTime? LastExecution { get; set; }

    public DateTime? DisabledAt { get; set; }

    [ConcurrencyCheck]
    public Guid Version { get; set; }
}
