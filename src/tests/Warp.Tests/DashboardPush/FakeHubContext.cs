using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Warp.UI.DashboardPush;

namespace Warp.Tests.DashboardPush;

/// <summary>
/// Test double for <see cref="IHubContext{THub}"/> that captures every <c>SendAsync</c> /
/// <c>SendCoreAsync</c> call to <c>Clients.All</c> into a thread-safe queue. Used across the
/// broadcaster unit tests and the integration tests as the assertion surface.
/// </summary>
public sealed class FakeHubContext : IHubContext<WarpDashboardHub>
{
    private readonly FakeHubClients _clients = new();

    public IHubClients Clients => _clients;

    public IGroupManager Groups { get; } = new NotSupportedGroupManager();

    public IReadOnlyCollection<(string Method, object?[] Args)> Broadcasts => _clients.AllProxy.Broadcasts;

    public int CountOf(string method) => Broadcasts.Count(x => string.Equals(x.Method, method, StringComparison.Ordinal));

    private sealed class FakeHubClients : IHubClients
    {
        public CapturingClientProxy AllProxy { get; } = new();

        public IClientProxy All => AllProxy;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;

        public IClientProxy Client(string connectionId) => AllProxy;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;

        public IClientProxy Group(string groupName) => AllProxy;

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => AllProxy;

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => AllProxy;

        public IClientProxy User(string userId) => AllProxy;

        public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
    }

    private sealed class CapturingClientProxy : IClientProxy
    {
        private readonly ConcurrentQueue<(string Method, object?[] Args)> _broadcasts = new();

        public IReadOnlyCollection<(string Method, object?[] Args)> Broadcasts => [.. _broadcasts];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _broadcasts.Enqueue((method, args));

            return Task.CompletedTask;
        }
    }

    private sealed class NotSupportedGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Groups are not used in v1 dashboard push.");

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Groups are not used in v1 dashboard push.");
    }
}
