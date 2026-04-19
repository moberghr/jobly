using Jobly.Core.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Mutation.Unit;

public class HandlerServiceExtensionsTests
{
    [Fact]
    public void AddJobHandlers_DoesNotRegisterUnrelatedGenericInterfaces()
    {
        // Arrange — TypeWithUnrelatedGenericInterface implements IComparable<string>
        // but NOT IJobHandler<>. It must not be registered.
        var services = new ServiceCollection();

        // Act
        services.AddJobHandlers(typeof(TypeWithUnrelatedGenericInterface).Assembly);

        // Assert — no IComparable<string> registration should exist
        var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IComparable<string>>();
        resolved.ShouldBeNull();
    }

    [Fact]
    public void AddJobHandlers_RegistersConcreteHandlerType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddJobHandlers(typeof(ConcreteTestHandler).Assembly);

        // Assert — concrete handler should be resolvable
        var descriptor = services.FirstOrDefault(x =>
            x.ServiceType == typeof(IJobHandler<ConcreteTestJob>));
        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(ConcreteTestHandler));
    }
}

public class ConcreteTestJob : IJob;

public class ConcreteTestHandler : IJobHandler<ConcreteTestJob>
{
    public Task HandleAsync(ConcreteTestJob message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class TypeWithUnrelatedGenericInterface : IComparable<string>
{
    public int CompareTo(string? other) => 0;
}
