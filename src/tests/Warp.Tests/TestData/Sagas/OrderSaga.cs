using Warp.Core.Sagas;

namespace Warp.Tests.TestData.Sagas;

public sealed class OrderSaga : Saga
{
    public string OrderId { get; set; } = string.Empty;

    public bool PaymentCaptured { get; set; }

    public bool InventoryReserved { get; set; }

    public bool TimedOut { get; set; }
}
