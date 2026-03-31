using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Handlers;
using Jobly.Test.Shared;
using Jobly.UI.UIMiddleware;
using Jobly.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddServices(builder.Configuration);
builder.Services.AddJobHandlers(typeof(Program).Assembly);
builder.Services.AddJobHandlers(typeof(Jobly.Test.Shared.ServiceConfiguration).Assembly);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddJoblyWorker<TestContext>(options =>
{
    options.RetryCount = 3;
    options.WorkerCount = 10;
    options.ServerName = "jobly-demo-server";
    options.DefaultQueue = "default";
    options.Queues = ["a-critical", "b-default", "c-low", "default"];
    options.PollingInterval = TimeSpan.FromMilliseconds(500);
    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
    options.HealthCheckTimeout = TimeSpan.FromSeconds(30);
    options.JobExpirationTimeout = TimeSpan.FromMinutes(30);
    options.UseDispatcher = true;
});

var app = builder.Build();

await Migrate();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseJoblyUI();
app.UseAuthorization();
app.MapControllers();

// Seed endpoint — creates a realistic demo workload
var seedQueues = new[] { "a-critical", "b-default", "c-low" };

app.MapPost("/seed", async (IPublisher publisher, IBatchPublisher batchPublisher, IRecurringJobPublisher recurringPublisher, TestContext context) =>
{
    var random = new Random();
    var queues = seedQueues;

    // === Jobs across queues (fast, will complete quickly) ===
    for (var i = 0; i < 300; i++)
    {
        var queue = queues[random.Next(queues.Length)];
        await publisher.Enqueue(new SendEmailRequest { EmailLogId = 1 }, queue);
    }

    // === Register jobs (each spawns child jobs inside handler — creates traces) ===
    for (var i = 0; i < 50; i++)
    {
        var queue = queues[random.Next(queues.Length)];
        await publisher.Enqueue(new RegisterRequest { Email = $"user{i}@test.com" }, queue);
    }

    // === Scheduled jobs (some past, some future) ===
    for (var i = 0; i < 50; i++)
    {
        var offset = random.Next(-60, 120);
        await publisher.Schedule(
            new RegisterRequest { Email = $"scheduled{i}@test.com" },
            DateTime.UtcNow.AddSeconds(offset));
    }

    // === Failing jobs (no retries — go straight to Failed) ===
    for (var i = 0; i < 30; i++)
    {
        await publisher.Enqueue(new ThrowExceptionRequest(), queues[random.Next(queues.Length)]);
    }

    // === Failing jobs with retries (shows retry lifecycle) ===
    for (var i = 0; i < 10; i++)
    {
        await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 3, queue: queues[random.Next(queues.Length)]);
    }

    // === Continuations (parent → child chains) ===
    for (var i = 0; i < 10; i++)
    {
        var parentId = await publisher.Enqueue(new RegisterRequest { Email = $"parent{i}@test.com" });
        await publisher.Enqueue(new RegisterRequest { Email = $"child{i}@test.com" }, parentId);
    }

    // === Slow job with awaiting children (visible for 30s) ===
    var slowJobId = await publisher.Enqueue(new SlowRequest());
    for (var i = 0; i < 5; i++)
    {
        await publisher.Enqueue(new SendEmailRequest { EmailLogId = 1 }, slowJobId);
    }

    // === Messages (pub/sub — each routes to multiple handlers) ===
    for (var i = 0; i < 10; i++)
    {
        await publisher.Publish(new OrderNotification());
    }

    // === Batch: 15 jobs → continuation batch of 8 (OnlyOnSucceeded, default) ===
    var batchJobs = Enumerable.Range(0, 15)
        .Select(_ => new SendEmailRequest { EmailLogId = 1 }).ToList();
    var batchId = await batchPublisher.StartNew(batchJobs);
    var batch2Jobs = Enumerable.Range(0, 8)
        .Select(_ => new SendEmailRequest { EmailLogId = 1 }).ToList();
    await batchPublisher.ContinueBatchWith(batch2Jobs, batchId);

    // === Batch with OnAnyFinishedState (continuation fires even if some fail) ===
    // Can't mix types in BatchPublisher, so use SendEmailRequest for success batch
    var failBatchJobs = Enumerable.Range(0, 5)
        .Select(_ => new ThrowExceptionRequest()).ToList();
    var failBatchId = await batchPublisher.StartNew(failBatchJobs, BatchContinuationOptions.OnAnyFinishedState);
    var afterFailBatchJobs = Enumerable.Range(0, 3)
        .Select(_ => new SendEmailRequest { EmailLogId = 1 }).ToList();
    await batchPublisher.ContinueBatchWith(afterFailBatchJobs, failBatchId);

    // === Complex flow: ProcessOrder → batch of ShipItem → PublishInvoice → InvoiceNotification message ===
    for (var i = 0; i < 5; i++)
    {
        await publisher.Enqueue(new ProcessOrderRequest { OrderId = $"ORD-{1000 + i}" });
    }

    // === Recurring jobs ===
    await recurringPublisher.AddOrUpdateRecurringJob(
        new SendEmailRequest { EmailLogId = 1 }, "send-daily-report", "0 9 * * *");
    await recurringPublisher.AddOrUpdateRecurringJob(
        new SendEmailRequest { EmailLogId = 1 }, "cleanup-hourly", "0 * * * *");

    await context.SaveChangesAsync();

    return Results.Ok(new
    {
        jobs = 300,
        registerJobs = 50,
        scheduled = 50,
        failing = 30,
        failingWithRetries = 10,
        continuations = 10,
        slowWithAwaiting = 6,
        messages = 10,
        orderFlows = 5,
        batches = 2,
        recurringJobs = 2,
    });
});

app.MapPost("/seed-perf", async (IPublisher publisher, TestContext context, int? count) =>
{
    var total = count ?? 10000;
    const int batchSize = 500;
    var created = 0;

    while (created < total)
    {
        var remaining = Math.Min(batchSize, total - created);
        for (var i = 0; i < remaining; i++)
        {
            await publisher.Enqueue(new EmptyRequest());
        }

        await context.SaveChangesAsync();
        created += remaining;
    }

    return Results.Ok(new { created });
});

app.MapGet("/seed-perf-batch", async (IBatchPublisher batchPublisher, TestContext context, int? jobsPerBatch, int? batchCount) =>
{
    var jobs = jobsPerBatch ?? 100;
    var batches = batchCount ?? 10;
    var totalJobs = 0;

    for (var b = 0; b < batches; b++)
    {
        var batchJobs = Enumerable.Range(0, jobs).Select(_ => new EmptyRequest()).ToList();
        await batchPublisher.StartNew(batchJobs);
        totalJobs += jobs;
    }

    await context.SaveChangesAsync();
    return Results.Ok(new { batches, jobsPerBatch = jobs, totalJobs });
});

app.MapGet("/seed-perf-messages", async (IPublisher publisher, TestContext context, int? count) =>
{
    var total = count ?? 100;
    for (var i = 0; i < total; i++)
    {
        await publisher.Publish(new EmptyMessage());
    }

    await context.SaveChangesAsync();
    return Results.Ok(new { messages = total, jobsPerMessage = 3, totalJobs = total * 3 });
});

app.MapPost("/perf-trace/enable", () =>
{
    Jobly.Worker.PerfTrace.Enable();
    return Results.Ok("Perf tracing enabled");
});

app.MapPost("/perf-trace/disable", () =>
{
    Jobly.Worker.PerfTrace.Disable();
    return Results.Ok("Perf tracing disabled");
});

app.MapGet("/perf-trace/dump", () =>
{
    var result = Jobly.Worker.PerfTrace.Dump();
    return Results.Text(result);
});

await app.RunAsync();

async Task Migrate()
{
    await using var scope = app!.Services.CreateAsyncScope();
    var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
    await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.EnsureCreatedAsync();
}
