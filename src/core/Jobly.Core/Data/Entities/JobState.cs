using System.ComponentModel.DataAnnotations;
using Jobly.Core.Enums;

namespace Jobly.Core.Entities;
public class JobState
{
    public Guid Id { get; set; }

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }

    [MaxLength(50)]
    public Guid JobId { get; set; }

    public Job Job { get; set; }
}
