using Microsoft.Extensions.Logging;
using Warp.Core.BackgroundServices;

namespace Warp.Test.Shared.Handlers.BackgroundServices;

/// <summary>
/// Demo per-server background service. Logs an incrementing tick every 5 seconds so the
/// dashboard's captured-log tail shows continuous activity. Demonstrates that
/// <see cref="ServiceScope.PerServer"/> services run independently on every server with
/// the binary deployed — start a second host and you'll see two instances both ticking.
/// </summary>
public sealed class TickCounterService : WarpBackgroundService
{
    private readonly ILogger<TickCounterService> _logger;

    public TickCounterService(ILogger<TickCounterService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var tick = 0;
        while (!ct.IsCancellationRequested)
        {
            tick++;
            _logger.LogInformation("Tick {Tick} at {Time:HH:mm:ss}", tick, DateTime.UtcNow);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TickCounterService stopped after {Tick} ticks", tick);

                return;
            }
        }
    }
}
