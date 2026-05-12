using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class ProgressReportingRequest : IJob;

public class ProgressReportingCommand(IJobContext context) : IJobHandler<ProgressReportingRequest>
{
    public Task HandleAsync(ProgressReportingRequest message, CancellationToken cancellationToken)
    {
        context.ReportProgress("download", 25);
        context.ReportProgress("download", 50);
        context.ReportProgress("download", 100);

        return Task.CompletedTask;
    }
}

public class MultiBarProgressRequest : IJob;

public class MultiBarProgressCommand(IJobContext context) : IJobHandler<MultiBarProgressRequest>
{
    public Task HandleAsync(MultiBarProgressRequest message, CancellationToken cancellationToken)
    {
        context.ReportProgress("download", 100);
        context.ReportProgress("process", 50);
        context.ReportProgress("upload", 10);

        return Task.CompletedTask;
    }
}

public class DedupProgressRequest : IJob;

public class DedupProgressCommand(IJobContext context) : IJobHandler<DedupProgressRequest>
{
    public Task HandleAsync(DedupProgressRequest message, CancellationToken cancellationToken)
    {
        context.ReportProgress("step", 42);
        context.ReportProgress("step", 42);
        context.ReportProgress("step", 42);

        return Task.CompletedTask;
    }
}

public class NoProgressRequest : IJob;

public class NoProgressCommand : IJobHandler<NoProgressRequest>
{
    public Task HandleAsync(NoProgressRequest message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class ThrowAfterProgressRequest : IJob;

public class ThrowAfterProgressCommand(IJobContext context) : IJobHandler<ThrowAfterProgressRequest>
{
    public Task HandleAsync(ThrowAfterProgressRequest message, CancellationToken cancellationToken)
    {
        // Report progress, then throw synchronously. The worker's exception-handling
        // path must still drain the progress collector via SaveProgressRows before
        // flipping the job to Failed.
        context.ReportProgress("phase", 50);
        throw new InvalidOperationException("test: handler throws after reporting progress");
    }
}

public class CancellableProgressRequest : IJob;

public class CancellableProgressCommand(IJobContext context, BarrierSignal signal) : IJobHandler<CancellableProgressRequest>
{
    public async Task HandleAsync(CancellableProgressRequest message, CancellationToken cancellationToken)
    {
        // Report progress synchronously, then park on the barrier. The test drives the rendezvous:
        //   1) test awaits signal.Running   → proves both ReportProgress calls have executed
        //   2) test issues DeleteJob        → worker cancels jobCts, WaitAsync below throws
        //   3) worker's cancellation branch drains progress in the same SaveChangesAsync as the
        //      "Cancelled" JobLog row, so assertions read a consistent snapshot.
        // No timeouts, no Task.Delay — pure handshake.
        context.ReportProgress("phase", 25);
        context.ReportProgress("phase", 50);

        signal.Running.Release();
        await signal.CanFinish.WaitAsync(cancellationToken);
    }
}
