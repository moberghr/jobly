using System.Text.Json;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Tests.TestData.Handlers;
using Jobly.Core.Retry;
using Shouldly;

namespace Jobly.Tests.Unit;

public class TypedMetadataTests
{
    [TimedFact]
    public void IRetryMetadata_GeneratedImpl_ExtendsFromDictionary()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.ShouldBeAssignableTo<Dictionary<string, object>>();
    }

    [TimedFact]
    public void IRetryMetadata_SetProperty_VisibleInDictionary()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.MaxRetries = 5;

        var dict = (Dictionary<string, object>)(object)impl;
        dict["MaxRetries"].ShouldBe(5);
    }

    [TimedFact]
    public void IRetryMetadata_SetInDictionary_VisibleViaProperty()
    {
        var dict = new Dictionary<string, object> { ["MaxRetries"] = 3 };
        var impl = MetadataFactory.Create<IRetryMetadata>(dict);

        impl.MaxRetries.ShouldBe(3);
    }

    [TimedFact]
    public void IRetryMetadata_ArrayProperty_RoundTrips()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.RetryDelays = [15, 60, 300];

        var dict = (Dictionary<string, object>)(object)impl;
        dict["RetryDelays"].ShouldBe(new int[] { 15, 60, 300 });

        impl.RetryDelays.Length.ShouldBe(3);
        impl.RetryDelays[0].ShouldBe(15);
    }

    [TimedFact]
    public void IRetryMetadata_DefaultValues_WhenKeysNotPresent()
    {
        var impl = MetadataFactory.Create<IRetryMetadata>([]);

        impl.MaxRetries.ShouldBeNull();
        impl.RetriedTimes.ShouldBe(0);
        impl.RetryDelays.ShouldBeNull();
    }

    [TimedFact]
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

    [TimedFact]
    public void ITestMetadata_GeneratedImpl_Works()
    {
        var impl = MetadataFactory.Create<ITestMetadata>([]);

        impl.TestKey = "hello";
        impl.TestCount = 42;

        var dict = (Dictionary<string, object>)(object)impl;
        dict["TestKey"].ShouldBe("hello");
        dict["TestCount"].ShouldBe(42);
    }

    [TimedFact]
    public void TypedView_WritesVisibleInOwnDictionary()
    {
        var dict = new Dictionary<string, object>();
        var retryMeta = MetadataFactory.Create<IRetryMetadata>(dict);

        retryMeta.MaxRetries = 3;

        // The impl IS the dictionary — cast and read
        var implDict = (Dictionary<string, object>)(object)retryMeta;
        implDict["MaxRetries"].ShouldBe(3);
    }

    [TimedFact]
    public void JobContext_GetMetadata_WritesFlowToUnderlyingDictionary()
    {
        var jobContext = new JobContext
        {
            JobId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object> { ["MaxRetries"] = 5 },
        };

        var typed = jobContext.GetMetadata<IRetryMetadata>();

        // Typed view IS the underlying dictionary
        ReferenceEquals(jobContext.Metadata, typed).ShouldBeTrue();

        // Read existing value
        typed.MaxRetries.ShouldBe(5);

        // Write via typed, read via raw
        typed.RetriedTimes = 1;
        jobContext.Metadata["RetriedTimes"].ShouldBe(1);

        // Write via raw, read via typed
        jobContext.Metadata["MaxRetries"] = 10;
        typed.MaxRetries.ShouldBe(10);
    }

    [TimedFact]
    public void PublishContext_GetMetadata_WritesFlowToUnderlyingDictionary()
    {
        var context = new PublishContext<object>
        {
            Job = new object(),
            Metadata = new Dictionary<string, object> { ["TestKey"] = "existing" },
        };

        var typed = context.GetMetadata<ITestMetadata>();

        // Typed view IS the underlying dictionary
        ReferenceEquals(context.Metadata, typed).ShouldBeTrue();

        // Existing value preserved
        typed.TestKey.ShouldBe("existing");

        // Write via typed, read via raw
        typed.TestCount = 42;
        context.Metadata["TestCount"].ShouldBe(42);
    }

    [TimedFact]
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

    [TimedFact]
    public void JobParameters_Configure_ChainedCalls_PreservesAllMetadata()
    {
        var parameters = new JobParameters()
            .Configure<IRetryMetadata>(m => m.MaxRetries = 10)
            .Configure<ITestMetadata>(m =>
            {
                m.TestKey = "hello";
                m.TestCount = 42;
            });

        parameters.Metadata.ShouldNotBeNull();
        parameters.Metadata["MaxRetries"].ShouldBe(10);
        parameters.Metadata["TestKey"].ShouldBe("hello");
        parameters.Metadata["TestCount"].ShouldBe(42);
    }

    [TimedFact]
    public void JobParameters_Configure_SingleCall_SetsMetadata()
    {
        var parameters = new JobParameters()
            .Configure<IRetryMetadata>(m =>
            {
                m.MaxRetries = 5;
                m.RetryDelays = [15, 60];
            });

        parameters.Metadata.ShouldNotBeNull();
        parameters.Metadata["MaxRetries"].ShouldBe(5);
        parameters.Metadata["RetryDelays"].ShouldBe(new int[] { 15, 60 });
    }
}
