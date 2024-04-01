using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

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