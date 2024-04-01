using Jobly.Core;
using Jobly.Test.App;
using Jobly.Test.Shared;
using Jobly.UI.UIMiddleware;
using Jobly.Worker;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddServices(builder.Configuration);

builder.Services.AddTransient<LoggingInterceptor>();

// Register Jobly worker
builder.Services.AddJoblyWorker<TestContext>(options =>
{
    options.WorkerCount = 10;
    // We get away with long polling because we are using a notify wakeup provider.
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.Interceptors.Add<LoggingInterceptor>();
});
builder.Services.AddPostgresNotifyWakeupProvider<TestContext>();

// Register Jobly Client
builder.Services.AddJobly<TestContext>(options =>
{
    options.RetryCount = 0;
});
builder.Services.AddPostgresNotifyJob<TestContext>();


var app = builder.Build();

// comment after db is created
await Migrate();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseJoblyUI();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

async Task Migrate()
{
    using var scope = app!.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
    await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.EnsureCreatedAsync();
}