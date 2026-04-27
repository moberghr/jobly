using System.Globalization;
using Warp.PerfTest;

// Defaults — bump via CLI args: --jobs 10000 --scenario dispatcher-push
var jobCount = 1000;
var filter = (string?)null;

var queue = new Queue<string>(args);
while (queue.Count > 0)
{
    var flag = queue.Dequeue();
    switch (flag)
    {
        case "--jobs" when queue.Count > 0:
            jobCount = int.Parse(queue.Dequeue(), CultureInfo.InvariantCulture);
            break;
        case "--scenario" when queue.Count > 0:
            filter = queue.Dequeue();
            break;
        default:
            break;
    }
}

var scenarios = new (string Name, bool UseDispatcher, bool EnableDatabasePush)[]
{
    ("workers-poll", false, false),
    ("dispatcher-poll", true, false),
    ("dispatcher-push", true, true),
};

var results = new List<PerfResult>();
foreach (var (name, useDispatcher, push) in scenarios)
{
    if (filter != null && !string.Equals(filter, name, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    await Console.Error.WriteLineAsync($"[{DateTime.UtcNow:HH:mm:ss}] Running scenario '{name}' with {jobCount} jobs...");
    var result = await PerfScenario.RunAsync(name, jobCount, useDispatcher, push);
    await Console.Error.WriteLineAsync($"[{DateTime.UtcNow:HH:mm:ss}]   duration={result.Duration}, total commands={result.Total}");
    results.Add(result);
}

// Print markdown table to stdout — redirect to docs/perf-results.md to commit.
await Console.Out.WriteLineAsync();
await Console.Out.WriteLineAsync("| Scenario          | Jobs  | Duration | SELECT | UPDATE | INSERT | DELETE | Other | Total |");
await Console.Out.WriteLineAsync("|-------------------|-------|----------|-------:|-------:|-------:|-------:|------:|------:|");
foreach (var r in results)
{
    var duration = r.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
    await Console.Out.WriteLineAsync(
        $"| {r.Name,-17} | {r.Jobs,5} | {duration,8} | {r.Select,6} | {r.Update,6} | {r.Insert,6} | {r.Delete,6} | {r.Other,5} | {r.Total,5} |");
}
