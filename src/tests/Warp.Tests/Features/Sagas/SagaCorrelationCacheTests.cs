using Shouldly;
using Warp.Core.Handlers;
using Warp.Core.Sagas;

namespace Warp.Tests.Features.Sagas;

[Trait("Category", "NoDb")]
public class SagaCorrelationCacheTests
{
    [TimedFact]
    public void GetCorrelationKey_PropertyMarkedCorrelate_ReturnsValue()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new GoodMessage { OrderId = "O-1" });

        key.ShouldBe("O-1");
    }

    [TimedFact]
    public void GetCorrelationKey_NoCorrelateProperty_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new MissingCorrelate()));
        ex.Message.ShouldContain("has no [Correlate] property");
    }

    [TimedFact]
    public void GetCorrelationKey_GuidProperty_CanonicalizesToN()
    {
        var cache = new SagaCorrelationCache();
        var id = Guid.Parse("11112222-3333-4444-5555-666677778888");

        var key = cache.GetCorrelationKey(new GuidCorrelate { OrderId = id });

        key.ShouldBe("11112222333344445555666677778888");
        key.Length.ShouldBe(32);
    }

    [TimedFact]
    public void GetCorrelationKey_IntProperty_CanonicalizesInvariant()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new IntCorrelate { OrderId = 42 });

        key.ShouldBe("42");
    }

    [TimedFact]
    public void GetCorrelationKey_LongProperty_CanonicalizesInvariant()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new LongCorrelate { OrderId = 9_000_000_000L });

        key.ShouldBe("9000000000");
    }

    [TimedFact]
    public void GetCorrelationKey_DefaultGuid_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new GuidCorrelate { OrderId = Guid.Empty }));
        ex.Message.ShouldContain("Guid.Empty");
    }

    [TimedFact]
    public void GetCorrelationKey_DefaultInt_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new IntCorrelate { OrderId = 0 }));
        ex.Message.ShouldContain("never assigned");
    }

    [TimedFact]
    public void GetCorrelationKey_DefaultLong_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new LongCorrelate { OrderId = 0L }));
        ex.Message.ShouldContain("never assigned");
    }

    [TimedFact]
    public void GetCorrelationKey_UnsupportedTypeProperty_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new DecimalCorrelate { OrderId = 1.5m }));
        ex.Message.ShouldContain("Supported correlation key types");
    }

    [TimedFact]
    public void GetCorrelationKey_MultipleCorrelateProperties_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() =>
            cache.GetCorrelationKey(new MultipleCorrelate { OrderId = "O-1", CustomerId = "C-1" }));
        ex.Message.ShouldContain("multiple [Correlate]");
    }

    [TimedFact]
    public void GetCorrelationKey_NullOrEmptyValue_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new GoodMessage { OrderId = string.Empty }));
        ex.Message.ShouldContain("empty");
    }

    [TimedFact]
    public void GetCorrelationKey_StringWithEmbeddedControlChar_PreservedAsLiteral()
    {
        // Trim removes leading/trailing whitespace including \r\n\t; embedded control chars
        // must NOT be normalized away — they're part of the user-supplied identifier and
        // round-trip storage + dashboard search must preserve them exactly.
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new GoodMessage { OrderId = "O-1\nO-2" });

        key.ShouldBe("O-1\nO-2");
    }

    [TimedFact]
    public void GetCorrelationKey_StringWithSurroundingWhitespace_TrimmedToCanonical()
    {
        var cache = new SagaCorrelationCache();

        var trimmed = cache.GetCorrelationKey(new GoodMessage { OrderId = "  O-1\n" });

        trimmed.ShouldBe("O-1");
    }

    [TimedFact]
    public void GetCorrelationKey_WhitespaceOnlyString_Throws()
    {
        var cache = new SagaCorrelationCache();

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new GoodMessage { OrderId = "   " }));
        ex.Message.ShouldContain("empty");
    }

    [TimedFact]
    public void GetCorrelationKey_StringAtMaxLength_Accepted()
    {
        var cache = new SagaCorrelationCache();
        var max = new string('x', SagaCorrelationKeyConverter.MaxKeyLength);

        var key = cache.GetCorrelationKey(new GoodMessage { OrderId = max });

        key.Length.ShouldBe(SagaCorrelationKeyConverter.MaxKeyLength);
    }

    [TimedFact]
    public void GetCorrelationKey_StringOverMaxLength_Throws()
    {
        var cache = new SagaCorrelationCache();
        var tooLong = new string('x', SagaCorrelationKeyConverter.MaxKeyLength + 1);

        var ex = Should.Throw<SagaConfigurationException>(() => cache.GetCorrelationKey(new GoodMessage { OrderId = tooLong }));
        ex.Message.ShouldContain("exceeds the maximum");
    }

    [TimedFact]
    public void GetCorrelationKey_LongMaxValue_RoundtripsExact()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new LongCorrelate { OrderId = long.MaxValue });

        key.ShouldBe(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SagaCorrelationKeyConverter.FromCanonical<long>(key).ShouldBe(long.MaxValue);
    }

    [TimedFact]
    public void GetCorrelationKey_LongMinValue_RoundtripsWithSign()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new LongCorrelate { OrderId = long.MinValue });

        SagaCorrelationKeyConverter.FromCanonical<long>(key).ShouldBe(long.MinValue);
    }

    [TimedFact]
    public void GetCorrelationKey_IntNegative_RoundtripsWithSign()
    {
        var cache = new SagaCorrelationCache();

        var key = cache.GetCorrelationKey(new IntCorrelate { OrderId = -42 });

        SagaCorrelationKeyConverter.FromCanonical<int>(key).ShouldBe(-42);
    }

    [TimedFact]
    public void GetCorrelationKey_CachedAcrossCalls_HappyPath()
    {
        var cache = new SagaCorrelationCache();

        cache.GetCorrelationKey(new GoodMessage { OrderId = "first" });
        cache.GetCorrelationKey(new GoodMessage { OrderId = "second" }).ShouldBe("second");
    }

    private sealed class GoodMessage : IMessage
    {
        [Correlate]
        public string OrderId { get; set; } = string.Empty;
    }

    private sealed class MissingCorrelate : IMessage
    {
        public string OrderId { get; set; } = string.Empty;
    }

    private sealed class IntCorrelate : IMessage
    {
        [Correlate]
        public int OrderId { get; set; }
    }

    private sealed class LongCorrelate : IMessage
    {
        [Correlate]
        public long OrderId { get; set; }
    }

    private sealed class GuidCorrelate : IMessage
    {
        [Correlate]
        public Guid OrderId { get; set; }
    }

    private sealed class DecimalCorrelate : IMessage
    {
        [Correlate]
        public decimal OrderId { get; set; }
    }

    private sealed class MultipleCorrelate : IMessage
    {
        [Correlate]
        public string OrderId { get; set; } = string.Empty;

        [Correlate]
        public string CustomerId { get; set; } = string.Empty;
    }
}
