using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Worker.Interceptors;

public class ContinuationInterceptor : JobInterceptor
{
    public override async Task JobExecutedAsync(JobExecutingContext context, CancellationToken cancellationToken)
    {
        var isParent = await context
            .DbContext.Set<Job>()
            .Where(x => x.ParentJobId == context.Job.Id)
            .AnyAsync(cancellationToken: cancellationToken);
        if (!isParent)
        {
            return;
        }
        
        // Check if other interceptors have changed the state of the job
        if (context.Job.CurrentState != State.Completed)
        {
            return;
        }
        
        await context.DbContext.Set<Job>()
            .Where(x => x.ParentJobId == context.Job.Id) // || x.ParentBatch.Job.ParentJobId == parentJobId) // todo: we should remove this parentBatch
            .Where(x => x.CurrentState == State.Awaiting)
            // If a job has Batch property in it, then it's a placeholder job, and we don't want to change current status of a placeholder job
            .Where(x => x.Batch == null) // todo: how do we start it then?
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.CurrentState, State.Enqueued), cancellationToken);
    }
}