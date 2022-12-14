using Microsoft.AspNetCore.Mvc;

namespace Handfire.Core.Controllers;

[Controller]
[ApiExplorerSettings(IgnoreApi = true)]
public class ScheduledController : Controller
{
    private readonly IHandfireService _handfireService;

    public ScheduledController(IHandfireService handfireService)
    {
        _handfireService = handfireService;
    }

    [HttpGet("scheduled")]
    public async Task<IActionResult> Index(BaseListRequest request)
    {
        var model = await _handfireService.GetScheduledJobs(request);

        return View(model);
    }
}