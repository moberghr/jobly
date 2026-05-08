using System.Text.Json;
using Shouldly;
using Warp.Core.Handlers;

namespace Warp.Tests.Core;

[Trait("Category", "NoDb")]
public class MetadataConvertTests
{
    private enum SampleMode
    {
        First = 1,
        Second = 2,
    }

    [TimedFact]
    public void To_Enum_FromLong_ReturnsEnumValue()
    {
        // NativeObjectConverter deserializes JSON numbers as long, so this is the dominant path.
        var result = MetadataConvert.To<SampleMode>(2L);

        result.ShouldBe(SampleMode.Second);
    }

    [TimedFact]
    public void To_NullableEnum_FromLong_ReturnsEnumValue()
    {
        var result = MetadataConvert.To<SampleMode?>(1L);

        result.ShouldBe(SampleMode.First);
    }

    [TimedFact]
    public void To_NullableEnum_FromNull_ReturnsNull()
    {
        var result = MetadataConvert.To<SampleMode?>(null);

        result.ShouldBeNull();
    }

    [TimedFact]
    public void To_Enum_FromBoxedEnum_ReturnsSameValue()
    {
        // In-memory path (publish-time write, read before any DB round trip).
        object boxed = SampleMode.Second;

        var result = MetadataConvert.To<SampleMode>(boxed);

        result.ShouldBe(SampleMode.Second);
    }

    [TimedFact]
    public void To_Enum_FromJsonElement_ReturnsEnumValue()
    {
        // After a JSON round trip without NativeObjectConverter (e.g. nested objects), the inner
        // values surface as JsonElement. The JsonElement branch handles that.
        var doc = JsonDocument.Parse("2");
        var element = doc.RootElement;

        var result = MetadataConvert.To<SampleMode>(element);

        result.ShouldBe(SampleMode.Second);
    }

    [TimedFact]
    public void To_Enum_FromString_ReturnsDefault()
    {
        // Defensive: a string under an enum-typed key shouldn't blow up the metadata accessor —
        // fall through to default(T) so the caller's `?? Fallback` recovers.
        var result = MetadataConvert.To<SampleMode>("Second");

        result.ShouldBe(default);
    }

    [TimedFact]
    public void To_NullableEnum_FromString_ReturnsNull()
    {
        var result = MetadataConvert.To<SampleMode?>("Second");

        result.ShouldBeNull();
    }
}
