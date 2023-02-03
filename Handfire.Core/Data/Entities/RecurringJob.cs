using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    public DateTime? UpdatedAt { get; set; }

    public DateTime? NextExecution { get; set; }

    public DateTime? LastExecution { get; set; }

    [MaxLength(50)]
    [ForeignKey(nameof(NextJob))]
    public string? NextJobId { get; set; }

    public Job? NextJob { get; set; }

    [MaxLength(50)]
    [ForeignKey(nameof(LastJob))]
    public string? LastJobId { get; set; }

    public Job? LastJob { get; set; }

    public ICollection<Job>? Jobs { get; set; }

    public uint Version { get; set; }
}
