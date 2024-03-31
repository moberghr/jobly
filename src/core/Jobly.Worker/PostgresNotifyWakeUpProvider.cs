using Jobly.Worker.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Jobly.Worker;


public class PostgresNotifyWakeupProvider<TContext> : IWakeupProvider where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;

    public PostgresNotifyWakeupProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ListenForUpdatesNotifications(CancellationToken cancellationToken, Action<WakeupType> action)
    {
        var channelName = "job_added"; // take from configuration
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection npgsqlConnection)
            throw new InvalidOperationException("Database connection must be of type NpgsqlConnection");

        // Ensure the connection is open
        if (npgsqlConnection.State != System.Data.ConnectionState.Open)
            await npgsqlConnection.OpenAsync(cancellationToken);
        
        npgsqlConnection.Notification += (o, e) =>
        {
            var payload = e.Payload;
            if (payload.StartsWith("batch"))
            {
                action(WakeupType.BatchAdded);
            } else if (payload.StartsWith("job"))
            {
                action(WakeupType.JobAdded);
            }
        };

        await using (var command = new NpgsqlCommand($"LISTEN {channelName};", npgsqlConnection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await npgsqlConnection.WaitAsync(cancellationToken);
        }
    }
}