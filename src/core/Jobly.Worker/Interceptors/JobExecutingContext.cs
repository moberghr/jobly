using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

/// <summary>
/// JobExecutingContext contains the information about the processing flow.
/// This context is still very much in progress and will be updated as we go.
/// Main thing here is to have access to the job and the DbContext running for the job.
/// </summary>
public class JobExecutingContext
{
    public JobExecutingContext(Job job, DbContext dbContext)
    {
        Job = job;
        DbContext = dbContext;
    }

    public Job Job { get; set; }
    
    public DbContext DbContext { get; set; }
}