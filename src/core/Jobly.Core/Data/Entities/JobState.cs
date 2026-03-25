using System.ComponentModel.DataAnnotations;
using Jobly.Core.Enums;

namespace Jobly.Core.Entities;
public class JobState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }
    
    public Guid JobId { get; set; }

    public Job Job { get; set; }
}
