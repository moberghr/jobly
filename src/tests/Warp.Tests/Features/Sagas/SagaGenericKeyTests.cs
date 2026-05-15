using Shouldly;
using Warp.Core.Sagas;

namespace Warp.Tests.Features.Sagas;

[Trait("Category", "NoDb")]
public class SagaGenericKeyTests
{
    [TimedFact]
    public void GuidKey_SetAndGet_RoundTrips()
    {
        var saga = new GuidSaga();
        var id = Guid.Parse("11112222-3333-4444-5555-666677778888");

        saga.Key = id;

        saga.CorrelationKey.ShouldBe("11112222333344445555666677778888"); // canonical "N" form
        saga.Key.ShouldBe(id);
    }

    [TimedFact]
    public void GuidKey_ReadFromCorrelationKey_ParsesCanonical()
    {
        // Simulates loading a persisted saga: the framework deserializes StateJson, sets
        // CorrelationKey from the row, and then user code reads .Key.
        var saga = new GuidSaga
        {
            CorrelationKey = "11112222333344445555666677778888",
        };

        saga.Key.ShouldBe(Guid.Parse("11112222-3333-4444-5555-666677778888"));
    }

    [TimedFact]
    public void IntKey_SetAndGet_RoundTrips()
    {
        var saga = new IntSaga { Key = 42 };

        saga.CorrelationKey.ShouldBe("42");
        saga.Key.ShouldBe(42);
    }

    [TimedFact]
    public void LongKey_SetAndGet_RoundTrips()
    {
        var saga = new LongSaga { Key = 9_000_000_000L };

        saga.CorrelationKey.ShouldBe("9000000000");
        saga.Key.ShouldBe(9_000_000_000L);
    }

    [TimedFact]
    public void StringKey_SetAndGet_IsIdentity()
    {
        var saga = new StringSaga { Key = "O-123" };

        saga.CorrelationKey.ShouldBe("O-123");
        saga.Key.ShouldBe("O-123");
    }

    [TimedFact]
    public void UnsupportedKey_OnRead_Throws()
    {
        var saga = new DecimalSaga { CorrelationKey = "1.5" };

        Should.Throw<SagaConfigurationException>(() => _ = saga.Key);
    }

    [TimedFact]
    public void UnsupportedKey_OnWrite_Throws()
    {
        var saga = new DecimalSaga();

        Should.Throw<SagaConfigurationException>(() => saga.Key = 1.5m);
    }

    [TimedFact]
    public void GuidKey_MatchesCacheCanonicalization()
    {
        // The contract: a [Correlate] Guid property on a message must produce the same canonical
        // string as Saga<Guid>.Key writes. This is the round-trip that makes typed correlation
        // work end-to-end — incoming message Guid is canonicalized to "N", saga is stored under
        // that string, saga.Key parses the same "N" back to the original Guid.
        var id = Guid.NewGuid();
        var saga = new GuidSaga { Key = id };
        var cache = new SagaCorrelationCache();
        var keyFromMessage = cache.GetCorrelationKey(new GuidMessage { OrderId = id });

        saga.CorrelationKey.ShouldBe(keyFromMessage);
    }

    private sealed class GuidSaga : Saga<Guid>;

    private sealed class IntSaga : Saga<int>;

    private sealed class LongSaga : Saga<long>;

    private sealed class StringSaga : Saga<string>;

    private sealed class DecimalSaga : Saga<decimal>;

    private sealed class GuidMessage : Warp.Core.Handlers.IMessage
    {
        [Correlate]
        public Guid OrderId { get; set; }
    }
}
