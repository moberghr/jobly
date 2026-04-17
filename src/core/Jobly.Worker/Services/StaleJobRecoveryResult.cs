namespace Jobly.Worker.Services;

public readonly record struct StaleJobRecoveryResult(int Requeued, int Failed, int Deleted)
{
    public int Total => Requeued + Failed + Deleted;
}
