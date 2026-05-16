using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Core;

[GenerateDatabaseTests]
public abstract class UtcDateTimeConverterTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected UtcDateTimeConverterTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task ReadingDateTime_AfterDbRoundTrip_HasKindUtc()
    {
        var now = DateTime.UtcNow;
        var serverId = Guid.NewGuid();

        var writeCtx = _fixture.CreateContext();
        writeCtx.Set<Server>().Add(new Server
        {
            Id = serverId,
            ServerName = "test",
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = 1,
        });
        await writeCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>()
            .AsNoTracking()
            .Where(x => x.Id == serverId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);

        server.StartedTime.Kind.ShouldBe(DateTimeKind.Utc);
        server.LastHeartbeatTime.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [TimedFact]
    public async Task NullableDateTime_AfterDbRoundTrip_HasKindUtc()
    {
        var now = DateTime.UtcNow;
        var serverId = Guid.NewGuid();

        var writeCtx = _fixture.CreateContext();
        writeCtx.Set<Server>().Add(new Server
        {
            Id = serverId,
            ServerName = "test",
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = 1,
            PausedAt = now,
        });
        await writeCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>()
            .AsNoTracking()
            .Where(x => x.Id == serverId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);

        server.PausedAt.ShouldNotBeNull();
        server.PausedAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [TimedFact]
    public async Task SerializedDateTime_AfterDbRoundTrip_RoundtripsAsUtc()
    {
        var now = DateTime.UtcNow;
        var serverId = Guid.NewGuid();

        var writeCtx = _fixture.CreateContext();
        writeCtx.Set<Server>().Add(new Server
        {
            Id = serverId,
            ServerName = "test",
            StartedTime = now,
            LastHeartbeatTime = now,
            ServiceCount = 1,
        });
        await writeCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>()
            .AsNoTracking()
            .Where(x => x.Id == serverId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);

        // The bug we're guarding against: JSON without a UTC marker is parsed as local time
        // by the browser. Verify the round-trip preserves Kind=Utc rather than asserting on
        // string shape — that way the test survives a JSON-serializer swap.
        var json = JsonSerializer.Serialize(server.StartedTime);
        var deserialized = JsonSerializer.Deserialize<DateTime>(json);

        deserialized.Kind.ShouldBe(DateTimeKind.Utc);
        deserialized.ShouldBe(server.StartedTime);
    }

    [TimedFact]
    public async Task WritingLocalKindDateTime_PreservesSameInstantAcrossRoundTrip()
    {
        // The write side converts Kind=Local → UTC via ToUniversalTime(). Verify the absolute
        // moment is preserved (not the wall-clock value) so any user code that ever assigns a
        // Local DateTime to a Warp entity isn't silently re-timestamped.
        var localNow = new DateTime(2026, 5, 16, 14, 30, 0, DateTimeKind.Local);
        var expectedUtc = localNow.ToUniversalTime();
        var serverId = Guid.NewGuid();

        var writeCtx = _fixture.CreateContext();
        writeCtx.Set<Server>().Add(new Server
        {
            Id = serverId,
            ServerName = "test",
            StartedTime = localNow,
            LastHeartbeatTime = localNow,
            ServiceCount = 1,
        });
        await writeCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var server = await readCtx.Set<Server>()
            .AsNoTracking()
            .Where(x => x.Id == serverId)
            .FirstAsync(Xunit.TestContext.Current.CancellationToken);

        server.StartedTime.Kind.ShouldBe(DateTimeKind.Utc);
        server.StartedTime.ShouldBe(expectedUtc);
    }
}
