using Handfire.Core;
using Handfire.Test.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddServices(builder.Configuration);
builder.Services.AddHandfire<TestContext>(10);

builder.Services.RegisterRazorServices();

var app = builder.Build();

// comment after db is created
await Migrate();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.AddHangfireDashboard();

app.Run();

async Task Migrate()
{
    using var scope = app!.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
    await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.EnsureCreatedAsync();
}