using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Concurrency;
using Warp.Core.Data.Entities;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Features.Concurrency;

[GenerateDatabaseTests]
public abstract class ConcurrencyLimitManagerTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected ConcurrencyLimitManagerTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task AddOrUpdateLimit_NewKey_InsertsRow()
    {
        // Arrange
        var fixedNow = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), timeProvider);

        // Act
        await manager.AddOrUpdateLimit("payment-api", 5, Xunit.TestContext.Current.CancellationToken);

        // Assert
        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<ConcurrencyLimit>()
            .Where(x => x.Name == "payment-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldNotBeNull();
        row.Limit.ShouldBe(5);
        row.UpdatedAt.ShouldBe(fixedNow);
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_ExistingKey_UpdatesLimitAndUpdatedAt()
    {
        // Arrange: pre-existing row
        var initial = new DateTime(2026, 5, 8, 10, 0, 0, DateTimeKind.Utc);
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "payment-api",
            Limit = 5,
            UpdatedAt = initial,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var later = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(later);
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), timeProvider);

        // Act
        await manager.AddOrUpdateLimit("payment-api", 10, Xunit.TestContext.Current.CancellationToken);

        // Assert
        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<ConcurrencyLimit>()
            .Where(x => x.Name == "payment-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldNotBeNull();
        row.Limit.ShouldBe(10);
        row.UpdatedAt.ShouldBe(later);

        var count = await readCtx.Set<ConcurrencyLimit>()
            .CountAsync(Xunit.TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

    [TimedFact]
    public async Task RemoveLimit_ExistingKey_DeletesRowReturnsTrue()
    {
        // Arrange
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "payment-api",
            Limit = 5,
            UpdatedAt = DateTime.UtcNow,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act
        var result = await manager.RemoveLimit("payment-api", Xunit.TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();

        var readCtx = _fixture.CreateContext();
        var row = await readCtx.Set<ConcurrencyLimit>()
            .Where(x => x.Name == "payment-api")
            .FirstOrDefaultAsync(Xunit.TestContext.Current.CancellationToken);
        row.ShouldBeNull();
    }

    [TimedFact]
    public async Task RemoveLimit_NonexistentKey_ReturnsFalse()
    {
        // Arrange
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act
        var result = await manager.RemoveLimit("missing", Xunit.TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [TimedFact]
    public async Task GetLimit_ExistingKey_ReturnsInfo()
    {
        // Arrange
        var updatedAt = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "payment-api",
            Limit = 5,
            UpdatedAt = updatedAt,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act
        var info = await manager.GetLimit("payment-api", Xunit.TestContext.Current.CancellationToken);

        // Assert
        info.ShouldNotBeNull();
        info.Name.ShouldBe("payment-api");
        info.Limit.ShouldBe(5);
        info.UpdatedAt.ShouldBe(updatedAt);
    }

    [TimedFact]
    public async Task GetLimit_NonexistentKey_ReturnsNull()
    {
        // Arrange
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act
        var info = await manager.GetLimit("missing", Xunit.TestContext.Current.CancellationToken);

        // Assert
        info.ShouldBeNull();
    }

    [TimedFact]
    public async Task ListLimits_ReturnsAllRowsOrderedByName()
    {
        // Arrange
        var seedCtx = _fixture.CreateContext();
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "charlie",
            Limit = 3,
            UpdatedAt = DateTime.UtcNow,
        });
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "alpha",
            Limit = 1,
            UpdatedAt = DateTime.UtcNow,
        });
        seedCtx.Set<ConcurrencyLimit>().Add(new ConcurrencyLimit
        {
            Name = "bravo",
            Limit = 2,
            UpdatedAt = DateTime.UtcNow,
        });
        await seedCtx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act
        var list = await manager.ListLimits(Xunit.TestContext.Current.CancellationToken);

        // Assert
        list.Count.ShouldBe(3);
        list[0].Name.ShouldBe("alpha");
        list[1].Name.ShouldBe("bravo");
        list[2].Name.ShouldBe("charlie");
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_NameEmpty_Throws()
    {
        // Arrange
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act + Assert
        await Should.ThrowAsync<ArgumentException>(() =>
            manager.AddOrUpdateLimit(string.Empty, 5, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_LimitZero_Throws()
    {
        // Arrange
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        // Act + Assert
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            manager.AddOrUpdateLimit("payment-api", 0, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task AddOrUpdateLimit_NameTooLong_Throws()
    {
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.AddOrUpdateLimit(tooLong, 5, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task RemoveLimit_NameEmpty_Throws()
    {
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.RemoveLimit(string.Empty, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task RemoveLimit_NameTooLong_Throws()
    {
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.RemoveLimit(tooLong, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task GetLimit_NameEmpty_Throws()
    {
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.GetLimit(string.Empty, Xunit.TestContext.Current.CancellationToken));
    }

    [TimedFact]
    public async Task GetLimit_NameTooLong_Throws()
    {
        var manager = new ConcurrencyLimitManager<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var tooLong = new string('x', 201);

        await Should.ThrowAsync<ArgumentException>(() =>
            manager.GetLimit(tooLong, Xunit.TestContext.Current.CancellationToken));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
