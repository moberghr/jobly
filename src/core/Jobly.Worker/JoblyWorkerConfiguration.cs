using System.Collections.ObjectModel;

namespace Jobly.Worker;

public class JoblyWorkerConfiguration
{
    public int WorkerCount { get; set; } = 10;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public IWakeupProvider? WakeupProvider { get; set; }

    public JoblyInterceptionConfiguration Interceptors { get; set; } = new();
}

public class JoblyInterceptionConfiguration : Collection<Type>
{
    public void Add<T>()
    {
        Add(typeof(T));
    }
}