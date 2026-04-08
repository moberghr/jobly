using System.Threading;
using System.Threading.Tasks;
using Jobly.Core.Handlers;

namespace Jobly.Benchmarks.JoblyLib;

public sealed class JoblyPingRequest : IRequest<JoblyPingResponse>
{
    public static readonly JoblyPingRequest Instance = new();
}

public sealed class JoblyPingResponse
{
    public static readonly JoblyPingResponse Instance = new();
}

public sealed class JoblyPingHandler : IRequestHandler<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(JoblyPingResponse.Instance);
    }
}

public sealed class JoblyPassthroughBehavior1 : IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, RequestHandlerDelegate<JoblyPingRequest, JoblyPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JoblyPassthroughBehavior2 : IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, RequestHandlerDelegate<JoblyPingRequest, JoblyPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JoblyPassthroughBehavior3 : IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, RequestHandlerDelegate<JoblyPingRequest, JoblyPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JoblyPassthroughBehavior4 : IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, RequestHandlerDelegate<JoblyPingRequest, JoblyPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JoblyPassthroughBehavior5 : IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>
{
    public Task<JoblyPingResponse> HandleAsync(JoblyPingRequest request, RequestHandlerDelegate<JoblyPingRequest, JoblyPingResponse> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}
