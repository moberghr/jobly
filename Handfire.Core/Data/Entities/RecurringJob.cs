using System.ComponentModel.DataAnnotations;
using Handfire.Core.Entities;

namespace Handfire.Core.Data.Entities;
public class RecurringJob
{
    public int Id { get; set; }
    
    public string Name { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public string Cron { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? NextExecution { get; set; }

    public DateTime? LastExecution { get; set; }

    public ICollection<Job>? Jobs { get; set; }
}
