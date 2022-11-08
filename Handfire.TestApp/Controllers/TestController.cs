using Handfire.Core.Handlers;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Handfire.TestApp.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    private readonly IMediator _mediator;

    public TestController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("register")]
    public async Task<RegisterResponse> Register(RegisterRequest request)
    {
        return await _mediator.Send(request);
    }
}