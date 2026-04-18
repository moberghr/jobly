using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Unit.NoRestart;

public class NoRestartPublishBehaviorTests
{
    private static async Task<ICanBeRestartedMetadata> RunBehavior<T>(T job, Dictionary<string, object>? startingMetadata = null)
    {
        var behavior = new NoRestartPublishBehavior<T>();
        var context = new PublishContext<T>
        {
            Job = job!,
            Metadata = startingMetadata ?? [],
        };

        await behavior.PublishAsync(context, () => Task.CompletedTask, CancellationToken.None);

        return context.GetMetadata<ICanBeRestartedMetadata>();
    }

    [TimedFact]
    public async Task PublishAsync_NoAttribute_MetadataUnchanged()
    {
        var meta = await RunBehavior(new UnitRequest());

        meta.CanBeRestarted.ShouldBeNull();
    }

    [TimedFact]
    public async Task PublishAsync_NoRestartAttribute_SetsMetadataFalse()
    {
        var meta = await RunBehavior(new NoRestartAttributeRequest());

        meta.CanBeRestarted.ShouldBe(false);
    }

    [TimedFact]
    public async Task PublishAsync_RestartAttribute_SetsMetadataTrue()
    {
        var meta = await RunBehavior(new RestartAttributeRequest());

        meta.CanBeRestarted.ShouldBe(true);
    }

    [TimedFact]
    public async Task PublishAsync_NoRestartAttributeOnBaseClass_IsInheritedByDerived()
    {
        // Regression for PR #125 review F2: [NoRestart] declared Inherited = false, so a base
        // class decorated with [NoRestart] would silently produce no effect on derived concrete
        // jobs. Since the feature exists specifically to protect non-idempotent operations
        // like payment or email jobs, that default is dangerous — a PaymentJobBase whose
        // derived concrete jobs do NOT inherit [NoRestart] would be auto-restarted after crash.
        var meta = await RunBehavior(new DerivedFromNoRestartBaseRequest());

        meta.CanBeRestarted.ShouldBe(false);
    }

    [TimedFact]
    public async Task PublishAsync_DerivedClassOverridesBaseAttribute_DerivedWins()
    {
        // Regression for the override case: when Inherited = true, a derived class with its
        // own [NoRestart] sees BOTH its own attribute AND the base's [Restart] via reflection
        // walk, which would trip the "both attributes" diagnostic. Derived must win.
        var meta = await RunBehavior(new DerivedOverridesBaseRestartRequest());

        meta.CanBeRestarted.ShouldBe(false);
    }

    [TimedFact]
    public async Task PublishAsync_MetadataAlreadySet_AttributeIgnored()
    {
        var starting = new Dictionary<string, object> { ["CanBeRestarted"] = true };
        var meta = await RunBehavior(new NoRestartAttributeRequest(), starting);

        meta.CanBeRestarted.ShouldBe(true);
    }

    [TimedFact]
    public async Task PublishAsync_BothAttributes_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => RunBehavior(new BothAttributesRequest()));
    }

    [TimedFact]
    public async Task PublishAsync_CallsNext()
    {
        var called = false;
        var behavior = new NoRestartPublishBehavior<UnitRequest>();
        var context = new PublishContext<UnitRequest>
        {
            Job = new UnitRequest(),
            Metadata = [],
        };

        await behavior.PublishAsync(
            context,
            () =>
            {
                called = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        called.ShouldBeTrue();
    }
}
