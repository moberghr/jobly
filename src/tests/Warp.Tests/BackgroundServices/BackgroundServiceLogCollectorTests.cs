using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Warp.Core.BackgroundServices;
using Warp.Core.Data.Entities;
using Warp.Worker.BackgroundServices;

namespace Warp.Tests.BackgroundServices;

[Trait("Category", "NoDb")]
public class BackgroundServiceLogCollectorTests
{
    private static readonly Guid TestServerId = Guid.NewGuid();
    private const string TestServiceName = "TestService";

    private static BackgroundServiceLogCollector CreateCollector(
        LogLevel minLogLevel = LogLevel.Information,
        FakeLogStore? store = null,
        FakeTimeProvider? time = null)
    {
        var actualStore = store ?? new FakeLogStore();
        var actualTime = time ?? new FakeTimeProvider(DateTime.UtcNow);
        var services = new ServiceCollection();
        services.AddSingleton<IBackgroundServiceLogStore>(actualStore);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new BackgroundServiceLogCollector(
            TestServiceName,
            TestServerId,
            minLogLevel,
            scopeFactory,
            actualTime,
            NullLogger.Instance);
    }

    [TimedFact]
    public async Task Enqueue_BelowMinLogLevel_NotBuffered()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Information, store);

        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Debug, "debug message", null);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Trace, "trace message", null);

        await collector.FlushAsync(CancellationToken.None);

        store.Inserted.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task Enqueue_AtOrAboveMinLogLevel_Buffered()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Warning, store);

        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Warning, "warn message", null);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Error, "error message", null);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, "info below threshold", null);

        await collector.FlushAsync(CancellationToken.None);

        store.Inserted.Count.ShouldBe(2);
        store.Inserted.ShouldContain(x => x.Message == "warn message");
        store.Inserted.ShouldContain(x => x.Message == "error message");
    }

    [TimedFact]
    public async Task Flush_DrainsBufferToBatchInsert()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Information, store);

        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, "msg1", null);
        collector.Enqueue(BackgroundServiceLogSource.Lifecycle, LogLevel.Information, "msg2", null);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Warning, "msg3", null);

        await collector.FlushAsync(CancellationToken.None);

        store.InsertCalls.ShouldBe(1);
        store.Inserted.Count.ShouldBe(3);

        await collector.FlushAsync(CancellationToken.None);

        store.InsertCalls.ShouldBe(1);
    }

    [TimedFact]
    public async Task Flush_EntriesHaveCorrectMetadata()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Information, store);

        var ex = new InvalidOperationException("oops");
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Error, "error msg", ex);

        await collector.FlushAsync(CancellationToken.None);

        var entry = store.Inserted.ShouldHaveSingleItem();
        entry.ServerId.ShouldBe(TestServerId);
        entry.ServiceName.ShouldBe(TestServiceName);
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Source.ShouldBe(BackgroundServiceLogSource.User);
        entry.Message.ShouldBe("error msg");
        entry.ExceptionType.ShouldBe("System.InvalidOperationException");
        entry.ExceptionMessage.ShouldNotBeNull();
        entry.ExceptionMessage.ShouldContain("oops");
    }

    [TimedFact]
    public async Task MessageOver4KB_Truncated_AppendsTruncationMarker()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Information, store);

        var longMessage = new string('A', 5000);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, longMessage, null);

        await collector.FlushAsync(CancellationToken.None);

        var entry = store.Inserted.ShouldHaveSingleItem();
        var utf8Length = System.Text.Encoding.UTF8.GetByteCount(entry.Message);
        utf8Length.ShouldBeLessThanOrEqualTo(4096);
        entry.Message.ShouldEndWith("…[truncated]");
    }

    [TimedFact]
    public async Task ExceptionMessageOver4KB_Truncated_AppendsTruncationMarker()
    {
        var store = new FakeLogStore();
        var collector = CreateCollector(LogLevel.Information, store);

        var longMessage = new string('B', 5000);
        var ex = new InvalidOperationException(longMessage);
        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Error, "msg", ex);

        await collector.FlushAsync(CancellationToken.None);

        var entry = store.Inserted.ShouldHaveSingleItem();
        entry.ExceptionMessage.ShouldNotBeNull();
        var utf8Length = System.Text.Encoding.UTF8.GetByteCount(entry.ExceptionMessage);
        utf8Length.ShouldBeLessThanOrEqualTo(4096);
        entry.ExceptionMessage.ShouldEndWith("…[truncated]");
    }

    [TimedFact]
    public async Task RateCap_SustainedAbove100PerSec_EntersDropModeAndEmitsWarning()
    {
        var store = new FakeLogStore();
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(now);
        var collector = CreateCollector(LogLevel.Information, store, time);

        for (var i = 0; i < 110; i++)
        {
            collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, $"msg-{i}", null);
        }

        await collector.FlushAsync(CancellationToken.None);

        var warningRows = store.Inserted
            .Where(x => x.Level == LogLevel.Warning)
            .Where(x => x.Source == BackgroundServiceLogSource.Lifecycle)
            .ToList();

        warningRows.Count.ShouldBe(1);
        warningRows[0].Message.ShouldContain("rate-limited");

        store.Inserted.Count.ShouldBe(101);
    }

    [TimedFact]
    public async Task RateCap_AfterDropWindow_ResumesAndEmitsSummary()
    {
        var store = new FakeLogStore();
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(now);
        var collector = CreateCollector(LogLevel.Information, store, time);

        for (var i = 0; i < 110; i++)
        {
            collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, $"msg-{i}", null);
        }

        time.AdvanceBy(TimeSpan.FromSeconds(11));

        collector.Enqueue(BackgroundServiceLogSource.User, LogLevel.Information, "after-resume", null);

        await collector.FlushAsync(CancellationToken.None);

        var summaryRows = store.Inserted
            .Where(x => x.Level == LogLevel.Information)
            .Where(x => x.Source == BackgroundServiceLogSource.Lifecycle)
            .Where(x => x.Message.Contains("resumed", StringComparison.Ordinal))
            .ToList();

        summaryRows.Count.ShouldBe(1);
        summaryRows[0].Message.ShouldContain("dropped");
        store.Inserted.ShouldContain(x => x.Message == "after-resume");
    }

    private sealed class FakeLogStore : IBackgroundServiceLogStore
    {
        public List<BackgroundServiceLog> Inserted { get; } = [];

        public int InsertCalls { get; private set; }

        public Task InsertManyAsync(IReadOnlyList<BackgroundServiceLog> entries, CancellationToken ct)
        {
            InsertCalls++;
            Inserted.AddRange(entries);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => new(_utcNow, TimeSpan.Zero);

        public void AdvanceBy(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
