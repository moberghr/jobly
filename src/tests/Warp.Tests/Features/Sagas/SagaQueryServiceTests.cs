using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Warp.Core;
using Warp.Core.Data.Entities;
using Warp.Core.Services;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Features.Sagas;

/// <summary>
/// DB-backed tests for <see cref="SagaQueryService{TContext}"/>. The proxy and integration tests
/// cover the active saga lifecycle; these tests cover the dashboard-facing queries that the
/// integration paths don't exercise (filter combinations, today-boundary stats, type listing).
/// </summary>
[GenerateDatabaseTests]
public abstract class SagaQueryServiceTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected SagaQueryServiceTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GetSagas_NoFilters_ReturnsAllOrderedByUpdatedAtDescending()
    {
        var arrangeCtx = _fixture.CreateContext();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 3; i++)
        {
            arrangeCtx.Set<SagaState>().Add(new SagaState
            {
                Id = Guid.NewGuid(),
                Type = "Test.A",
                CorrelationKey = $"k-{i}",
                StateJson = "{}",
                CreatedAt = baseTime,
                UpdatedAt = baseTime.AddMinutes(i),
            });
        }

        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var page = await svc.GetSagas(new BaseListRequest { Page = 0, PageSize = 10 }, type: null, correlationKeyContains: null);

        page.TotalCount.ShouldBe(3);
        page.Items.Count.ShouldBe(3);
        page.Items.Select(s => s.CorrelationKey).ShouldBe(["k-2", "k-1", "k-0"]);
    }

    [TimedFact]
    public async Task GetSagas_FilterByType_OnlyReturnsMatchingType()
    {
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            NewSaga("Test.Order", "ord-1"),
            NewSaga("Test.Order", "ord-2"),
            NewSaga("Test.Shipment", "ship-1"));
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var page = await svc.GetSagas(new BaseListRequest { Page = 0, PageSize = 10 }, type: "Test.Order", correlationKeyContains: null);

        page.TotalCount.ShouldBe(2);
        page.Items.ShouldAllBe(s => s.Type == "Test.Order");
    }

    [TimedFact]
    public async Task GetSagas_FilterByCorrelationKeyContains_LikeMatch()
    {
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            NewSaga("Test.A", "alpha"),
            NewSaga("Test.A", "alphabet"),
            NewSaga("Test.A", "beta"));
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var page = await svc.GetSagas(new BaseListRequest { Page = 0, PageSize = 10 }, type: null, correlationKeyContains: "alpha");

        page.TotalCount.ShouldBe(2);
        page.Items.ShouldAllBe(s => s.CorrelationKey.Contains("alpha"));
    }

    [TimedFact]
    public async Task GetSagas_PercentInFilter_TreatedAsLiteralNotWildcard()
    {
        // Without LIKE-escape, a "%" filter matches every row. With the escape, it should match
        // only rows whose correlation key actually contains a "%".
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            NewSaga("Test.A", "100%-okay"),
            NewSaga("Test.A", "plain"),
            NewSaga("Test.A", "more-plain"));
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var page = await svc.GetSagas(new BaseListRequest { Page = 0, PageSize = 10 }, type: null, correlationKeyContains: "%");

        page.TotalCount.ShouldBe(1);
        page.Items[0].CorrelationKey.ShouldBe("100%-okay");
    }

    [TimedFact]
    public async Task GetSagas_UnderscoreInFilter_TreatedAsLiteralNotSingleCharWildcard()
    {
        // SQL LIKE _ matches any single character; the escape must convert it to a literal.
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            NewSaga("Test.A", "abc_def"),
            NewSaga("Test.A", "abcXdef"),
            NewSaga("Test.A", "abcYdef"));
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var page = await svc.GetSagas(new BaseListRequest { Page = 0, PageSize = 10 }, type: null, correlationKeyContains: "_");

        page.TotalCount.ShouldBe(1);
        page.Items[0].CorrelationKey.ShouldBe("abc_def");
    }

    [TimedFact]
    public async Task GetSagaById_KnownId_ReturnsDetailWithStateJsonAndVersion()
    {
        var sagaId = Guid.NewGuid();
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().Add(new SagaState
        {
            Id = sagaId,
            Type = "Test.WithState",
            CorrelationKey = "detail-1",
            StateJson = "{\"Counter\":42}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var detail = await svc.GetSagaById(sagaId);

        detail.ShouldNotBeNull();
        detail.Id.ShouldBe(sagaId);
        detail.CorrelationKey.ShouldBe("detail-1");
        detail.StateJson.ShouldBe("{\"Counter\":42}");
        detail.Version.ShouldNotBe(Guid.Empty);
    }

    [TimedFact]
    public async Task GetSagaById_UnknownId_ReturnsNull()
    {
        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);

        var detail = await svc.GetSagaById(Guid.NewGuid());

        detail.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetSagaTypes_ReturnsDistinctOrderedTypes()
    {
        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            NewSaga("Bravo.Saga", "k1"),
            NewSaga("Bravo.Saga", "k2"),
            NewSaga("Alpha.Saga", "k3"),
            NewSaga("Charlie.Saga", "k4"));
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var types = await svc.GetSagaTypes();

        types.ShouldBe(["Alpha.Saga", "Bravo.Saga", "Charlie.Saga"]);
    }

    [TimedFact]
    public async Task GetStats_CountsLiveAndStartedTodayCorrectly()
    {
        // Frozen "now" = 2026-05-13 12:00 UTC; "today start" = 2026-05-13 00:00 UTC.
        // Three sagas: one created yesterday, two created today.
        var now = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);
        var yesterday = now.AddDays(-1);
        var time = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));

        var arrangeCtx = _fixture.CreateContext();
        arrangeCtx.Set<SagaState>().AddRange(
            new SagaState { Id = Guid.NewGuid(), Type = "T", CorrelationKey = "old", StateJson = "{}", CreatedAt = yesterday, UpdatedAt = yesterday },
            new SagaState { Id = Guid.NewGuid(), Type = "T", CorrelationKey = "t1", StateJson = "{}", CreatedAt = now, UpdatedAt = now },
            new SagaState { Id = Guid.NewGuid(), Type = "T", CorrelationKey = "t2", StateJson = "{}", CreatedAt = now.AddMinutes(-5), UpdatedAt = now });
        await arrangeCtx.SaveChangesAsync(TestCancellation);

        var svc = new SagaQueryService<TestContext>(_fixture.CreateContext(), time);
        var stats = await svc.GetStats();

        stats.LiveSagas.ShouldBe(3);
        stats.StartedToday.ShouldBe(2);
        stats.CompletedToday.ShouldBe(0);
    }

    private static SagaState NewSaga(string type, string key) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        CorrelationKey = key,
        StateJson = "{}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static CancellationToken TestCancellation => Xunit.TestContext.Current.CancellationToken;
}
