using System.Threading;
using System.Threading.Tasks;
using Jobly.Core.Handlers;

namespace Jobly.Benchmarks.JoblyLib;

public sealed class BenchmarkJob : IJob
{
    public static readonly BenchmarkJob Instance = new();
}

public sealed class BenchmarkJobHandler : IJobHandler<BenchmarkJob>
{
    public Task HandleAsync(BenchmarkJob message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class JobBehavior1 : IPipelineBehavior<BenchmarkJob, Unit>
{
    public Task<Unit> HandleAsync(BenchmarkJob request, RequestHandlerDelegate<BenchmarkJob, Unit> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JobBehavior2 : IPipelineBehavior<BenchmarkJob, Unit>
{
    public Task<Unit> HandleAsync(BenchmarkJob request, RequestHandlerDelegate<BenchmarkJob, Unit> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JobBehavior3 : IPipelineBehavior<BenchmarkJob, Unit>
{
    public Task<Unit> HandleAsync(BenchmarkJob request, RequestHandlerDelegate<BenchmarkJob, Unit> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JobBehavior4 : IPipelineBehavior<BenchmarkJob, Unit>
{
    public Task<Unit> HandleAsync(BenchmarkJob request, RequestHandlerDelegate<BenchmarkJob, Unit> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}

public sealed class JobBehavior5 : IPipelineBehavior<BenchmarkJob, Unit>
{
    public Task<Unit> HandleAsync(BenchmarkJob request, RequestHandlerDelegate<BenchmarkJob, Unit> next, CancellationToken cancellationToken)
    {
        return next(request, cancellationToken);
    }
}
