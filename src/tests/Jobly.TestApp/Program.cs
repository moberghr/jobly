using Jobly.Core;
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
    options.DefaultQueue = "default";
    options.Queues = new[] { "a-critical", "b-default", "c-low", "default" };
    options.PollingInterval = TimeSpan.FromMilliseconds(500);
    options.HealthCheckInterval = TimeSpan.FromSeconds(10);
    options.HealthCheckTimeout = TimeSpan.FromSeconds(30);
    options.JobExpirationTimeout = TimeSpan.FromMinutes(30);
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

// Seed endpoint — creates thousands of jobs for demo
app.MapPost("/seed", async (IPublisher publisher, TestContext context) =>
{
    var random = new Random();
    var queues = new[] { "a-critical", "b-default", "c-low" };

    // 500 jobs across queues
    for (var i = 0; i < 500; i++)
    {
        var queue = queues[random.Next(queues.Length)];
        await publisher.Enqueue(new Jobly.Core.Handlers.SendEmailRequest { EmailLogId = 1 }, queue);
    }

    // 300 more jobs
    for (var i = 0; i < 300; i++)
    {
        var queue = queues[random.Next(queues.Length)];
        await publisher.Enqueue(new Jobly.Core.Handlers.RegisterRequest { Email = $"user{i}@test.com" }, queue);
    }

    // 100 scheduled jobs (some in the future, some in the past)
    for (var i = 0; i < 100; i++)
    {
        var offset = random.Next(-60, 120); // -60s to +120s
        await publisher.Schedule(
            new Jobly.Core.Handlers.RegisterRequest { Email = $"scheduled{i}@test.com" },
            DateTime.UtcNow.AddSeconds(offset));
    }

    // 50 jobs that will fail
    for (var i = 0; i < 50; i++)
    {
        await publisher.Enqueue(new Jobly.Core.Handlers.ThrowExceptionRequest(),
            queues[random.Next(queues.Length)]);
    }

    // 20 continuations (parent → child chains)
    for (var i = 0; i < 20; i++)
    {
        var parentId = await publisher.Enqueue(
            new Jobly.Core.Handlers.RegisterRequest { Email = $"parent{i}@test.com" });
        await publisher.Enqueue(
            new Jobly.Core.Handlers.RegisterRequest { Email = $"child{i}@test.com" },
            parentId);
    }

    await context.SaveChangesAsync();

    return Results.Ok(new { messages = 500, jobs = 300, scheduled = 100, failing = 50, continuations = 20 });
});

app.Run();

async Task Migrate()
{
    using var scope = app!.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
    await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.EnsureCreatedAsync();
}
