using Jobly.Core.Handlers;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Jobly.TestApp.Controllers;

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

    [HttpPost("register-schedule")]
    public async Task<ScheduleRegisterResponse> ScheduleRegister(ScheduleRegisterRequest request)
    {
        return await _mediator.Send(request);
    }

    [HttpPost("failed-job")]
    public async Task<FailedJobResponse> FailedJob(FailedJobRequest request)
    {
        return await _mediator.Send(request);
    }

    [HttpPost("recurring-job")]
    public async Task<RecurringJobResponse> RecurringJob(RecurringJobRequest request)
    {
        return await _mediator.Send(request);
    }
}