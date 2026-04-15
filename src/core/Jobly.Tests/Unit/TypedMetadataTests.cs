using System.Text.Json;
using Jobly.Core.Handlers;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Retry;
using Shouldly;

namespace Jobly.Tests.Unit;

public class TypedMetadataTests
{
    [Fact]
    public void IRetryMetadata_GeneratedImpl_ExtendsFromDictionary()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.ShouldBeAssignableTo<Dictionary<string, object>>();
    }

    [Fact]
    public void IRetryMetadata_SetProperty_VisibleInDictionary()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.MaxRetries = 5;

        var dict = (Dictionary<string, object>)(object)impl;
        dict["MaxRetries"].ShouldBe(5);
    }

    [Fact]
    public void IRetryMetadata_SetInDictionary_VisibleViaProperty()
    {
        var dict = new Dictionary<string, object> { ["MaxRetries"] = 3 };
        var impl = MetadataFactory.Create<IRetryMetadata>(dict);

        impl.MaxRetries.ShouldBe(3);
    }

    [Fact]
    public void IRetryMetadata_ArrayProperty_RoundTrips()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.RetryDelays = [15, 60, 300];

        var dict = (Dictionary<string, object>)(object)impl;
        dict["RetryDelays"].ShouldBe(new int[] { 15, 60, 300 });

        impl.RetryDelays.Length.ShouldBe(3);
        impl.RetryDelays[0].ShouldBe(15);
    }

    [Fact]
    public void IRetryMetadata_DefaultValues_WhenKeysNotPresent()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.MaxRetries.ShouldBeNull();
        impl.RetriedTimes.ShouldBe(0);
        impl.RetryDelays.ShouldBeNull();
    }

    [Fact]
    public void IRetryMetadata_FromSerializedJson_ConvertsNativeTypes()
    {
        var json = """{"MaxRetries":3,"RetriedTimes":1,"RetryDelays":[15,60]}""";
        var dict = MetadataSerializer.Deserialize(json);
        var impl = MetadataFactory.Create<IRetryMetadata>(dict);

        impl.MaxRetries.ShouldBe(3);
        impl.RetriedTimes.ShouldBe(1);
        impl.RetryDelays.ShouldNotBeNull();
        impl.RetryDelays.Length.ShouldBe(2);
    }

    [Fact]
    public void ITestMetadata_GeneratedImpl_Works()
    {
        var impl = MetadataFactory.Create<ITestMetadata>([]);

        impl.TestKey = "hello";
        impl.TestCount = 42;

        var dict = (Dictionary<string, object>)(object)impl;
        dict["TestKey"].ShouldBe("hello");
        dict["TestCount"].ShouldBe(42);
    }

    [Fact]
    public void TypedView_WritesVisibleInOwnDictionary()
    {
        var dict = new Dictionary<string, object>();
        var retryMeta = MetadataFactory.Create<IRetryMetadata>(dict);

        retryMeta.MaxRetries = 3;

        // The impl IS the dictionary — cast and read
        var implDict = (Dictionary<string, object>)(object)retryMeta;
        implDict["MaxRetries"].ShouldBe(3);
    }

    [Fact]
    public void TypedView_ReplacesDictOnJobContext_SameObject()
    {
        var jobContext = new JobContext
        {
            JobId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object> { ["MaxRetries"] = 5 },
        };

        // Simulate what JobContext<T> does
        var typed = MetadataFactory.Create<IRetryMetadata>(jobContext.Metadata);
        jobContext.Metadata = (Dictionary<string, object>)(object)typed;

        // Typed and raw are the same object
        ReferenceEquals(jobContext.Metadata, typed).ShouldBeTrue();

        // Read via typed
        typed.MaxRetries.ShouldBe(5);

        // Write via typed, read via raw
        typed.RetriedTimes = 1;
        jobContext.Metadata["RetriedTimes"].ShouldBe(1);

        // Write via raw, read via typed
        jobContext.Metadata["MaxRetries"] = 10;
        typed.MaxRetries.ShouldBe(10);
    }

    [Fact]
    public void Serialize_TypedMetadata_ProducesCorrectJson()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);
        impl.MaxRetries = 3;
        impl.RetriedTimes = 1;
        impl.RetryDelays = [15, 60];

        var dict = (Dictionary<string, object>)(object)impl;
        var json = JsonSerializer.Serialize(dict);

        json.ShouldContain("\"MaxRetries\":3");
        json.ShouldContain("\"RetriedTimes\":1");
        json.ShouldContain("\"RetryDelays\":[15,60]");
    }
}
