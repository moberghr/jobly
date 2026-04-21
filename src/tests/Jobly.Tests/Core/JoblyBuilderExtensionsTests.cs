using Jobly.Core;
using Jobly.Tests.TestData;
using Jobly.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Core;

// Covers BindConfiguration — the appsettings.json bridge on the Jobly builder. Uses a
// MemoryConfigurationSource so the test is pure-unit (no appsettings.json on disk). Builder
// inherits JoblyConfiguration fields, so we assert via the IJoblyBuilder.Configuration
// surface (the same instance) and via the worker-specific fields that live on the concrete
// builder type.
[Trait("Category", "NoDb")]
public class JoblyBuilderExtensionsTests
{
    [Fact]
    public void BindConfiguration_PopulatesFieldsFromSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobly:DefaultQueue"] = "custom",
                ["Jobly:Schema"] = "myschema",
                ["Jobly:JobExpirationTimeout"] = "02:00:00",
            })
            .Build();

        var builder = new JoblyBuilder<TestContext>(new ServiceCollection());

        builder.BindConfiguration(config.GetSection("Jobly"));

        builder.DefaultQueue.ShouldBe("custom");
        builder.Schema.ShouldBe("myschema");
        builder.JobExpirationTimeout.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void BindConfiguration_UnknownKeys_AreIgnored()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobly:DefaultQueue"] = "custom",
                ["Jobly:NotARealField"] = "ignored",
            })
            .Build();

        var builder = new JoblyBuilder<TestContext>(new ServiceCollection());

        Should.NotThrow(() => builder.BindConfiguration(config.GetSection("Jobly")));
        builder.DefaultQueue.ShouldBe("custom");
    }

    [Fact]
    public void BindConfiguration_ReturnsSameBuilderInstance()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        var builder = new JoblyBuilder<TestContext>(new ServiceCollection());
        IJoblyBuilder<TestContext> asInterface = builder;

        var returned = asInterface.BindConfiguration(config.GetSection("Jobly"));

        returned.ShouldBeSameAs(builder);
    }

    // Pins that BindConfiguration hits worker-specific fields on the worker builder. The
    // appsettings.json flow is the primary use case, and most of its value is setting
    // WorkerCount / PollingInterval / HealthCheckTimeout, which only exist on the worker
    // subclass — not on the base JoblyConfiguration.
    [Fact]
    public void BindConfiguration_PopulatesWorkerFields()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobly:WorkerCount"] = "7",
                ["Jobly:PollingInterval"] = "00:00:05",
                ["Jobly:HealthCheckTimeout"] = "00:10:00",
            })
            .Build();

        var builder = new JoblyWorkerBuilder<TestContext>(new ServiceCollection());

        builder.BindConfiguration(config.GetSection("Jobly"));

        builder.WorkerCount.ShouldBe(7);
        builder.PollingInterval.ShouldBe(TimeSpan.FromSeconds(5));
        builder.HealthCheckTimeout.ShouldBe(TimeSpan.FromMinutes(10));
    }
}
