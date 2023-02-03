using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;

namespace Handfire.Tests.TestData.Handlers;
public class ThrowExceptionCommand : IRequestHandler<ThrowExceptionRequest, Unit>
{
    public Task<Unit> Handle(ThrowExceptionRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class ThrowExceptionRequest : IRequest<Unit>
{

}
