using System.Runtime.InteropServices;

namespace Jobly.Worker.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct StaleJobRecoveryResult(int Requeued, int Failed, int Deleted)
{
    public int Total => Requeued + Failed + Deleted;
}
