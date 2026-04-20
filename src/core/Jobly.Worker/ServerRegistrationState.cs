namespace Jobly.Worker;

/// <summary>
/// In-process singleton that carries the DB-generated WorkerGroup IDs and assigned worker IDs
/// from <see cref="JoblyServerRegistration{TContext}"/> to the worker host services. All three
/// are <see cref="Microsoft.Extensions.Hosting.IHostedService"/> implementations running in the
/// same process; startup order is guaranteed by DI registration order in <c>AddJoblyWorker</c>.
/// </summary>
public sealed class ServerRegistrationState
{
    public IReadOnlyList<GroupRegistration> Groups { get; private set; } = [];

    public void Set(IReadOnlyList<GroupRegistration> groups)
    {
        Groups = groups;
    }

    public sealed record GroupRegistration(
        WorkerGroupConfiguration Config,
        Guid GroupEntityId,
        IReadOnlyList<Guid> WorkerIds);
}
