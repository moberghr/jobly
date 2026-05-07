using Shouldly;
using Warp.Http.Discovery;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class AttributeDiscoveryTests
{
    [TimedFact]
    public void Snapshot_IncludesEveryTaggedRequestType()
    {
        var snapshot = WarpGeneratedHttpRegistry.Snapshot();

        snapshot.ShouldContain(d => d.RequestType == typeof(DiscoveryGetRecord));
        snapshot.ShouldContain(d => d.RequestType == typeof(DiscoveryPostClass));
        snapshot.ShouldContain(d => d.RequestType == typeof(DiscoveryStreamFeed));
    }

    [TimedFact]
    public void Snapshot_PicksUpVerbFromAttributeSubclass()
    {
        var get = WarpGeneratedHttpRegistry.Snapshot()
            .Single(d => d.RequestType == typeof(DiscoveryGetRecord));

        get.Method.ShouldBe("GET");
        get.Route.ShouldBe("/discovery/get-record");
    }

    [TimedFact]
    public void Snapshot_RecordsExplicitGroup()
    {
        var post = WarpGeneratedHttpRegistry.Snapshot()
            .Single(d => d.RequestType == typeof(DiscoveryPostClass));

        post.Group.ShouldBe("discovery");
    }

    [TimedFact]
    public void Snapshot_ClassifiesIRequestAsRequestKind()
    {
        var post = WarpGeneratedHttpRegistry.Snapshot()
            .Single(d => d.RequestType == typeof(DiscoveryPostClass));

        post.Kind.ShouldBe(HandlerKind.Request);
        post.ResponseType.ShouldBe(typeof(int));
    }

    [TimedFact]
    public void Snapshot_ClassifiesIStreamRequestAsStreamKind()
    {
        var stream = WarpGeneratedHttpRegistry.Snapshot()
            .Single(d => d.RequestType == typeof(DiscoveryStreamFeed));

        stream.Kind.ShouldBe(HandlerKind.Stream);
        stream.ResponseType.ShouldBe(typeof(int));
    }

    [TimedFact]
    public void Snapshot_DefaultGroupIsNull()
    {
        var get = WarpGeneratedHttpRegistry.Snapshot()
            .Single(d => d.RequestType == typeof(DiscoveryGetRecord));

        get.Group.ShouldBeNull();
    }
}
