using Microsoft.AspNetCore.Mvc;

namespace Jobly.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class ScheduledController : Controller
{
    private readonly IJoblyService _handfireService;

    public ScheduledController(IJoblyService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("scheduled")]
    public async Task<IActionResult> Index(BaseListRequest request)
    {
        var model = await _handfireService.GetScheduledJobs(request);

        return Ok(model);
    }
}