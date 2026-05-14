using Shouldly;
using Warp.Core.Handlers;
using Warp.Core.Helper;
using Warp.Core.Timeout;

namespace Warp.Tests.Features.Timeout;

[Trait("Category", "NoDb")]
public class TimeoutAttributeTests
{
    [TimedFact]
    public Task TimeoutAttribute_Positive_SetsSeconds()
    {
        var attr = new TimeoutAttribute(30);

        attr.Seconds.ShouldBe(30);
        attr.Mode.ShouldBe(TimeoutMode.Delete);
        attr.Scope.ShouldBe(TimeoutScope.PerAttempt);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task TimeoutAttribute_Zero_ThrowsAtConstruction()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TimeoutAttribute(0));
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task TimeoutAttribute_Negative_ThrowsAtConstruction()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new TimeoutAttribute(-5));
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task TimeoutAttribute_ModeAndScopeInit_AreReadOnce()
    {
        var attr = new TimeoutAttribute(10)
        {
            Mode = TimeoutMode.Fail,
            Scope = TimeoutScope.Total,
        };

        attr.Mode.ShouldBe(TimeoutMode.Fail);
        attr.Scope.ShouldBe(TimeoutScope.Total);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_Positive_SetsAllMetadataFields()
    {
        var parameters = new JobParameters().WithTimeout(TimeSpan.FromSeconds(15));

        parameters.Metadata.ShouldNotBeNull();
        var meta = MetadataFactory.Create<ITimeoutMetadata>(parameters.Metadata);
        meta.TimeoutSeconds.ShouldBe(15);
        meta.TimeoutMode.ShouldBe(TimeoutMode.Delete);
        meta.TimeoutScope.ShouldBe(TimeoutScope.PerAttempt);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_RoundsUpFractionalSeconds()
    {
        var parameters = new JobParameters().WithTimeout(TimeSpan.FromMilliseconds(1500));

        parameters.Metadata.ShouldNotBeNull();
        var meta = MetadataFactory.Create<ITimeoutMetadata>(parameters.Metadata);
        meta.TimeoutSeconds.ShouldBe(2);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_FailMode_PersistsMode()
    {
        var parameters = new JobParameters().WithTimeout(TimeSpan.FromSeconds(5), TimeoutMode.Fail);

        var meta = MetadataFactory.Create<ITimeoutMetadata>(parameters.Metadata!);
        meta.TimeoutMode.ShouldBe(TimeoutMode.Fail);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_TotalScope_PersistsScope()
    {
        var parameters = new JobParameters().WithTimeout(TimeSpan.FromSeconds(5), TimeoutMode.Fail, TimeoutScope.Total);

        var meta = MetadataFactory.Create<ITimeoutMetadata>(parameters.Metadata!);
        meta.TimeoutScope.ShouldBe(TimeoutScope.Total);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_Zero_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new JobParameters().WithTimeout(TimeSpan.Zero));
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithTimeout_Negative_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new JobParameters().WithTimeout(TimeSpan.FromSeconds(-1)));
        return Task.CompletedTask;
    }
}
