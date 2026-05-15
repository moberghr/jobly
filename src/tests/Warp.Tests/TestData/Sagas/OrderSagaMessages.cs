using Warp.Core.Handlers;
using Warp.Core.Sagas;

namespace Warp.Tests.TestData.Sagas;

[StartsSaga]
public sealed class OrderPlaced : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class PaymentCaptured : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class InventoryReserved : IMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;
}

public sealed class OrderTimeout : ITimeoutMessage
{
    [Correlate]
    public string OrderId { get; set; } = string.Empty;

    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(500);
}
