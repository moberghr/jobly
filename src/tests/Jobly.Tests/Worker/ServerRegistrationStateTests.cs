using Jobly.Worker;
using Shouldly;

namespace Jobly.Tests.Worker;

[Trait("Category", "NoDb")]
public class ServerRegistrationStateTests
{
    [Fact]
    public void Set_FirstCall_PopulatesGroups()
    {
        var state = new ServerRegistrationState();
        var group = new ServerRegistrationState.GroupRegistration(
            new WorkerGroupConfiguration { Queues = ["default"], WorkerCount = 1 },
            Guid.NewGuid(),
            [Guid.NewGuid()]);

        state.Set([group]);

        state.Groups.Count.ShouldBe(1);
        state.Groups[0].ShouldBe(group);
    }

    [Fact]
    public void Set_SecondCall_Throws()
    {
        var state = new ServerRegistrationState();
        var group = new ServerRegistrationState.GroupRegistration(
            new WorkerGroupConfiguration { Queues = ["default"], WorkerCount = 1 },
            Guid.NewGuid(),
            [Guid.NewGuid()]);
        state.Set([group]);

        Should.Throw<InvalidOperationException>(() => state.Set([group]));
    }

    [Fact]
    public void Groups_BeforeSet_IsEmpty()
    {
        var state = new ServerRegistrationState();

        state.Groups.Count.ShouldBe(0);
    }
}
