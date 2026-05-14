using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.RateLimit;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Features.RateLimit;

[GenerateDatabaseTests]
public abstract class RateLimitManagerTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RateLimitManagerTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task AddOrUpdateLimit_NewKey_InsertsRow()
    {
        var fixedNow = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), timeProvider);

        await manager.AddOrUpdateLimit("external-api", 100, 60, Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<RateLimitOverride>()
            .Where(x => x.Name == "external-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldNotBeNull();
        row.Count.ShouldBe(100);
        row.WindowSeconds.ShouldBe(60);
        row.UpdatedAt.ShouldBe(fixedNow);
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_ExistingKey_UpdatesAllFields()
    {
        var initial = new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc);
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride
        {
            Name = "external-api",
            Count = 100,
            WindowSeconds = 60,
            UpdatedAt = initial,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var later = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), new FakeTimeProvider(later));

        await manager.AddOrUpdateLimit("external-api", 50, 30, Xunit.TestContext.Current.CancellationToken);

        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<RateLimitOverride>()
            .Where(x => x.Name == "external-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldNotBeNull();
        row.Count.ShouldBe(50);
        row.WindowSeconds.ShouldBe(30);
        row.UpdatedAt.ShouldBe(later);

        var count = await readCtx.Set<RateLimitOverride>()
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

    [TimedFact]
    public async Task RemoveLimit_ExistingKey_DeletesAndReturnsTrue()
    {
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride
        {
            Name = "external-api",
            Count = 100,
            WindowSeconds = 60,
            UpdatedAt = DateTime.UtcNow,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var result = await manager.RemoveLimit("external-api", Xunit.TestContext.Current.CancellationToken);

        result.ShouldBeTrue();

        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<RateLimitOverride>()
            .Where(x => x.Name == "external-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldBeNull();
    }

    [TimedFact]
    public async Task RemoveLimit_NonexistentKey_ReturnsFalse()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var result = await manager.RemoveLimit("missing", Xunit.TestContext.Current.CancellationToken);

        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task GetLimit_ExistingKey_ReturnsInfo()
    {
        var updatedAt = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride
        {
            Name = "external-api",
            Count = 100,
            WindowSeconds = 60,
            UpdatedAt = updatedAt,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var info = await manager.GetLimit("external-api", Xunit.TestContext.Current.CancellationToken);

        info.ShouldNotBeNull();
        info.Name.ShouldBe("external-api");
        info.Count.ShouldBe(100);
        info.WindowSeconds.ShouldBe(60);
        info.UpdatedAt.ShouldBe(updatedAt);
    }

    [TimedFact]
    public async Task GetLimit_NonexistentKey_ReturnsNull()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var info = await manager.GetLimit("missing", Xunit.TestContext.Current.CancellationToken);

        info.ShouldBeNull();
    }

    [TimedFact]
    public async Task ListLimits_ReturnsAllOrderedByName()
    {
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride { Name = "charlie", Count = 3, WindowSeconds = 30, UpdatedAt = DateTime.UtcNow });
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride { Name = "alpha", Count = 1, WindowSeconds = 10, UpdatedAt = DateTime.UtcNow });
        seedCtx.Set<RateLimitOverride>().Add(new RateLimitOverride { Name = "bravo", Count = 2, WindowSeconds = 20, UpdatedAt = DateTime.UtcNow });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var list = await manager.ListLimits(Xunit.TestContext.Current.CancellationToken);

        list.Count.ShouldBe(3);
        list[0].Name.ShouldBe("alpha");
        list[1].Name.ShouldBe("bravo");
        list[2].Name.ShouldBe("charlie");
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_NameEmpty_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.AddOrUpdateLimit(string.Empty, 5, 60, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_CountZero_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            manager.AddOrUpdateLimit("external-api", 0, 60, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_WindowZero_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            manager.AddOrUpdateLimit("external-api", 5, 0, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_WindowExceedsMax_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            manager.AddOrUpdateLimit("external-api", 5, RateLimitAttribute.MaxWindowSeconds + 1, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_NameTooLong_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.AddOrUpdateLimit(tooLong, 5, 60, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task RemoveLimit_NameEmpty_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.RemoveLimit(string.Empty, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task RemoveLimit_NameTooLong_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.RemoveLimit(tooLong, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task GetLimit_NameEmpty_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.GetLimit(string.Empty, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task GetLimit_NameTooLong_Throws()
    {
        var manager = new RateLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.GetLimit(tooLong, Xunit.TestContext.Current.CancellationToken));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
