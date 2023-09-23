using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class ScheduledController : Controller
{
    private readonly IJoblyService _joblyService;

    public ScheduledController(IJoblyService joblyService)
    {
        _joblyService = joblyService;
    }

    [HttpGet("scheduled")]
    public async Task<IActionResult> Index(BaseListRequest request)
    {
        var model = await _joblyService.GetScheduledJobs(request);

        return Ok(model);
    }
}