using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Core.NoRestart;
using Shouldly;

namespace Jobly.Tests.Features.NoRestart;

[Trait("Category", "NoDb")]
public class NoRestartExtensionTests
{
    [TimedFact]
    public Task WithRestart_True_SetsMetadataTrue()
    {
        var parameters = new JobParameters().WithRestart(canBeRestarted: true);

        parameters.Metadata.ShouldNotBeNull();
        var meta = MetadataFactory.Create<ICanBeRestartedMetadata>(parameters.Metadata);
        meta.CanBeRestarted.ShouldBe(true);
        return Task.CompletedTask;
    }

    [TimedFact]
    public Task WithRestart_False_SetsMetadataFalse()
    {
        var parameters = new JobParameters().WithRestart(canBeRestarted: false);

        parameters.Metadata.ShouldNotBeNull();
        var meta = MetadataFactory.Create<ICanBeRestartedMetadata>(parameters.Metadata);
        meta.CanBeRestarted.ShouldBe(false);
        return Task.CompletedTask;
    }
}
