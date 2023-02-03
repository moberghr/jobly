using System.ComponentModel.DataAnnotations;
using Handfire.Core.Enums;

namespace Handfire.Core.Entities;
public class JobState
{
    public int Id { get; set; }

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }

    [MaxLength(50)]
    public string JobId { get; set; }

    public Job Job { get; set; }
}
