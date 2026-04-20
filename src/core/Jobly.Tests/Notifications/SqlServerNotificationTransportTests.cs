using Jobly.Core.Notifications;
using Jobly.Tests.Fixtures;
using Shouldly;

namespace Jobly.Tests.Notifications;

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class SqlServerNotificationTransportTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public SqlServerNotificationTransportTests(SqlServerFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private SqlServerNotificationTransport CreateTransport(string channelName) =>
        new(_fixture.ConnectionString, new JoblyDatabasePushConfiguration { ChannelName = channelName });

    [TimedFact]
    public async Task PublishListen_RoundTrip_DeliversNotification()
    {
        var transport = CreateTransport("jobly_notify_rt");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var enumerator = transport.ListenAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        try
        {
            var moveTask = enumerator.MoveNextAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);

            await transport.PublishAsync(NotificationKind.JobEnqueued, "default", cts.Token);

            var hasNext = await moveTask;
            hasNext.ShouldBeTrue("Expected to receive a notification within the timeout");
            enumerator.Current.Kind.ShouldBe(NotificationKind.JobEnqueued);
            enumerator.Current.Queue.ShouldBe("default");
        }
        finally
        {
            await cts.CancelAsync();
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [TimedFact]
    public async Task PublishListen_MultipleKinds_DeliversAll()
    {
        var transport = CreateTransport("jobly_notify_multi");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var enumerator = transport.ListenAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        try
        {
            var firstMoveTask = enumerator.MoveNextAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);

            await transport.PublishAsync(NotificationKind.MessageEnqueued, null, cts.Token);
            await transport.PublishAsync(NotificationKind.JobFinalized, null, cts.Token);
            await transport.PublishAsync(NotificationKind.JobEnqueued, "critical", cts.Token);

            var received = new List<Notification>();
            var hasNext = await firstMoveTask;
            hasNext.ShouldBeTrue();
            received.Add(enumerator.Current);

            while (received.Count < 3)
            {
                hasNext = await enumerator.MoveNextAsync();
                hasNext.ShouldBeTrue();
                received.Add(enumerator.Current);
            }

            received.ShouldContain(n => n.Kind == NotificationKind.MessageEnqueued);
            received.ShouldContain(n => n.Kind == NotificationKind.JobFinalized);
            received.ShouldContain(n => n.Kind == NotificationKind.JobEnqueued && n.Queue == "critical");
        }
        finally
        {
            await cts.CancelAsync();
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public void Constructor_UnsafeChannelName_Throws()
    {
        var unsafe1 = () => new SqlServerNotificationTransport(
            _fixture.ConnectionString,
            new JoblyDatabasePushConfiguration { ChannelName = "bad; drop table foo;" });
        unsafe1.ShouldThrow<ArgumentException>();

        var unsafe2 = () => new SqlServerNotificationTransport(
            _fixture.ConnectionString,
            new JoblyDatabasePushConfiguration { ChannelName = "1starts_with_digit" });
        unsafe2.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Encode_Decode_RoundTripsJobEnqueuedWithQueue()
    {
        var payload = SqlServerNotificationTransport.Encode(NotificationKind.JobEnqueued, "critical");
        SqlServerNotificationTransport.TryDecode(payload, out var parsed).ShouldBeTrue();
        parsed.Kind.ShouldBe(NotificationKind.JobEnqueued);
        parsed.Queue.ShouldBe("critical");
    }

    [Fact]
    public void Decode_Garbage_ReturnsFalse()
    {
        SqlServerNotificationTransport.TryDecode("Xxx", out _).ShouldBeFalse();
        SqlServerNotificationTransport.TryDecode(null, out _).ShouldBeFalse();
        SqlServerNotificationTransport.TryDecode(string.Empty, out _).ShouldBeFalse();
    }
}
