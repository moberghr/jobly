using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;

namespace Handfire.Tests.TestData.Handlers;
public class UnitCommand : IRequestHandler<UnitRequest, Unit>
{
    public Task<Unit> Handle(UnitRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Unit.Value);
    }
}

public class UnitRequest : IRequest<Unit>
{

}
