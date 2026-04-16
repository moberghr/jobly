using BenchmarkDotNet.Running;
using Jobly.ServerBenchmarks.Benchmarks;

if (args.Length > 0 && string.Equals(args[0], "stress", StringComparison.OrdinalIgnoreCase))
{
    var workers = 10;
    var jobsPerRound = 10_000;
    var rounds = 10;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i].StartsWith("--workers=", StringComparison.OrdinalIgnoreCase))
        {
            workers = int.Parse(args[i]["--workers=".Length..]);
        }
        else if (args[i].StartsWith("--jobs=", StringComparison.OrdinalIgnoreCase))
        {
            jobsPerRound = int.Parse(args[i]["--jobs=".Length..]);
        }
        else if (args[i].StartsWith("--rounds=", StringComparison.OrdinalIgnoreCase))
        {
            rounds = int.Parse(args[i]["--rounds=".Length..]);
        }
    }

    await MemoryStressTest.RunAsync(workers, jobsPerRound, rounds);
}
else
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
