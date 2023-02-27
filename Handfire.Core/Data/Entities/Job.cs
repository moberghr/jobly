using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using Handfire.Core.Data.Entities;
using Handfire.Core.Enums;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core.Entities;

public class Job
{
    [MaxLength(50)]
    public string Id { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ScheduleTime { get; set; }

    public State CurrentState { get; set; }

    public int? RecurringJobId { get; set; }

    public int RetriedTimes { get; set; }

    public int MaxRetries { get; set; }

    public string? ParentJobId { get; set; }

    public RecurringJob? RecurringJob { get; set; }

    public List<JobState> JobStates { get; set; } = new();

    public Job? ParentJob { get; set; }

    public List<Job> ChildJobs { get; set; } = new();
}
